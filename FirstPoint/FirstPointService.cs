using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Timers;
using HugeLib;

namespace FirstPoint
{
    public partial class FirstPointService : ServiceBase
    {
        private string _startDirectory, _exeName;
        private XmlDocument _xmlSettings = null, _xmlProducts = null;
        private string _inDirectory, _inMask;
        private Timer _mainTimer = null;
        List<ProductClass> _products = new List<ProductClass>();
        public FirstPointService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            Start();
        }

        public void Start()
        {
            LogClass.ToConsole = true;
            _startDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
            _exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName.Replace(".exe", "").Replace(".vshost", "");
            _xmlSettings = new XmlDocument();
            try
            {
                _xmlSettings.Load($"{_startDirectory}{_exeName}.xml");
            }
            catch (Exception exc)
            { 
                LogClass.WriteToLog($"Ошибка загрузки настроек: {exc.Message}");
                return;
            }
            _inDirectory = XmlClass.GetTag(_xmlSettings, "InputDirectory", null);
            _inMask = XmlClass.GetAttribute(_xmlSettings, "InputDirectory", "Mask", "*", null);

            _xmlProducts = new XmlDocument();
            try
            {
                string prods = XmlClass.GetTag(_xmlSettings, "ProductsFile", null).Replace("..\\", _startDirectory);
                if (prods.Length == 0)
                    prods = $"{_startDirectory}Products.xml";
                _xmlProducts.Load(prods);
                int cnt = XmlClass.GetXmlNodeCount(_xmlProducts, "", null);
                _products.Clear();
                for (int i = 0; i < cnt; i++)
                {
                    ProductClass pc = new ProductClass();
                    XmlDocument pr = XmlClass.GetXmlNode(_xmlProducts, "", i, null);
                    pc.ProductName = XmlClass.GetAttribute(pr, "", "Name", null);
                    pc.conditions = new List<List<Condition>>();
                    int allConditionsCnt = XmlClass.GetXmlNodeCount(pr, "Conditions", null);
                    for (int k = 0; k < allConditionsCnt; k++)
                    {
                        pc.conditions.Add(new List<Condition>());
                        XmlDocument oneConditions = XmlClass.GetXmlNode(pr, "Conditions", k, null);
                        int conditionCnt = XmlClass.GetXmlNodeCount(oneConditions, "Condition", null);
                        for (int j = 0; j < conditionCnt; j++)
                        {
                            XmlDocument cn = XmlClass.GetXmlNode(oneConditions, "Condition", j, null);
                            pc.conditions[k].Add(new Condition() { Field = XmlClass.GetAttribute(cn, "", "Field", null), Type = XmlClass.GetAttribute(cn, "", "Type", null), Value = XmlClass.GetAttribute(cn, "", "Value", null) });
                        }
                    }
                    XmlDocument addon = XmlClass.GetXmlNode(pr, "AdditionalProcessing", 0, null);
                    if (addon != null)
                        pc.AdditionalXml = addon.OuterXml;
                    pc.Ignore = XmlClass.GetAttribute(pr, "", "Ignore", "false", null).ToLower() == "true";
                    _products.Add(pc);
                }
            }
            catch (Exception exc)
            {
                LogClass.WriteToLog($"Ошибка загрузки файла продуктов: {exc.Message}");
                return;
            }


            if (!Directory.Exists(_inDirectory))
            {
                LogClass.WriteToLog($"Ошибка открытия входной директории: {_inDirectory}");
                return;
            }

            _mainTimer = new Timer {Interval = 5000, AutoReset = true};
            _mainTimer.Elapsed += _mainTimer_Elapsed;
        }

        private void _mainTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _mainTimer.Stop();
            DirectoryInfo di = new DirectoryInfo(_inDirectory);
            string deviceField = XmlClass.GetAttribute(_xmlSettings, "Devices", "InputField", null);
            foreach (FileInfo file in di.GetFiles(_inMask))
            {
                using (StreamReader sr = new StreamReader(file.Name, Encoding.GetEncoding(1251)))
                {
                    int nom = 0;
                    sr.BaseStream.Seek(0, SeekOrigin.Begin);
                    while (sr.Peek() >= 0)
                    {
                        RecordClass rc = new RecordClass();
                        rc.OriginalString = sr.ReadLine();
                        rc.FileName = file.Name;
                        rc.ParseFields(_xmlSettings);
                        bool check = false, ignore = false;

                        foreach (ProductClass pc in _products)
                        {
                            foreach (List<Condition> cc in pc.conditions)
                            {
                                foreach (Condition c in cc)
                                {
                                    check = rc.CheckCondition(c.Field, c.Type, c.Value);
                                    if (!check)
                                        break;
                                }
                                if (check)
                                    break;
                            }
                            if (check)
                            {
                                if (ignore = pc.Ignore) //здесь мы присваиваем значение и сразу проверяем. Не прозрачно, но нормально
                                    break;

                                string device = XmlClass.GetXmlAttribute(_xmlSettings, "Devices/Device", "Name",
                                    rc.GetField(deviceField), "Alias", null);
                                


                                using (SqlConnection conn = new SqlConnection())
                                {
                                    conn.ConnectionString = "Server = Stas10; Database = CardRoute; Uid = sa; Pwd = 123;";
                                    conn.Open();
                                    
                                    using (SqlCommand comm = conn.CreateCommand())
                                    {
                                        comm.CommandText = $"insert into Cards (CRN, CardData, DeviceDesignId, CardStatusId, CardPriorityId, StepN, JobId) values " +
                                                           $"(@crn, @data, @dd, @status, @priority, @step, 1)";
                                        comm.Parameters.Add("@crn", SqlDbType.NChar, 64);
                                        comm.Parameters.Add("@data", SqlDbType.NVarChar);
                                        comm.Parameters.Add("@dd", SqlDbType.Int);
                                        comm.Parameters.Add("@status", SqlDbType.Int);
                                        comm.Parameters.Add("@priority", SqlDbType.Int);
                                        comm.Parameters.Add("@step", SqlDbType.Int);

                                        string xml = "<?xml version='1.0' encoding='utf-16'?><Data>##DATA##</Data>";

                                        for (int i = 0; i < 20; i++)
                                        {
                                            int p = r.Next(1, 999999999);
                                            string pan = $"{bins[r.Next(3)]}{p:0000000000}";
                                            comm.Parameters["@crn"].Value = pan;
                                            comm.Parameters["@data"].Value = xml.Replace("##PAN##", pan).Replace("##DATE##", "2207")
                                                .Replace("##NAME##", names[r.Next(names.Count)]);
                                            comm.Parameters["@dd"].Value = r.Next(2) + 1;
                                            comm.Parameters["@status"].Value = 1;
                                            comm.Parameters["@priority"].Value = 2;
                                            comm.Parameters["@step"].Value = 1;
                                            comm.ExecuteNonQuery();
                                        }
                                    }

                                    conn.Close();
                                }
                                    if (newFile)
                                    {
                                        string workname = String.Format(@"{0}Pending\{1}.{2}", startDirectory, prefix, pc.ProductName);
                                        StreamWriter sw = new StreamWriter(workname, true, Encoding.GetEncoding(enc));
                                        sw.WriteLine(PreProcessing(rc.OriginalString, prior));
                                        sw.Close();
                                        if (rc.AdvanceString.Length > 0)
                                        {
                                            string advname = String.Format(@"{0}Pending\{1}.{2}.adv", startDirectory, prefix, pc.ProductName);
                                            StreamWriter swa = new StreamWriter(advname, true, Encoding.GetEncoding(enc));
                                            swa.WriteLine(rc.AdvanceString);
                                            swa.Close();
                                        }
                                    }
                                if (ht.ContainsKey(pc.ProductName))
                                    ht[pc.ProductName] = (int)ht[pc.ProductName] + 1;
                                else
                                    ht.Add(pc.ProductName, 1);
                                break;
                            }
                        }
                        if (!check)
                        {
                            if (newFile)
                            {
                                string workname = String.Format(@"{0}Pending\{1}.{2}", startDirectory, prefix, "notfound");
                                StreamWriter sw = new StreamWriter(workname, true);
                                sw.WriteLine(rc.OriginalString);
                                sw.Close();
                            }
                            ht[""] = (int)ht[""] + 1;
                        }
                        if (!ignore)
                            res.Count++;
                    }
                    sr.Close();
                }
            }
            _mainTimer.Start();
        }

        protected override void OnStop()
        {
        }
    }
}
