using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Xml;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Serialization;
using System.Resources;
using System.Security.Cryptography;
using System.Windows;
using Media = System.Windows.Media;
using System.Windows.Media.Imaging;
//using HugeLib;
using DataPrep;
using Devices;
using DpclDevice;
using ProcardWPF;
using Brushes = System.Drawing.Brushes;
using Timer = System.Timers.Timer;



namespace CardRoute
{
    extern alias DpclMy;
    extern alias stasHugeLib;

    public partial class CardRouteService : ServiceBase
    {
        //https://www.codeproject.com/Articles/55890/Don-t-hard-code-your-DataProviders
        private string connectionString;

        ResourceManager resourceManager = new ResourceManager("CardRoute.Properties.Resources", typeof(CardRouteService).Assembly);

        XmlDocument xmlDoc = null;
        XmlNamespaceManager xnm = null;
        
        private bool stopFlag = false;
        private static long threadCount = 0;

        private Timer timerStart = null;
        private Timer timerCdp = null;
        private Timer timerIssue = null;
        private Timer timerReport = null;
        private Timer timerCentral = null;
        private Timer timerPin = null;

        private int timerInterval = 5000;
        private string lang = "russian";
        private string protocol = "https";

        private DbProviderFactory factory = null;

        Hashtable htIssue = new Hashtable();

        public CardRouteService()
        {
            InitializeComponent();
        }

        public void Start()
        {
#if DEBUG
            stasHugeLib::HugeLib.LogClass.ToConsole = true;
#endif
            stasHugeLib::HugeLib.LogClass.AddThread = true;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.CreateSpecificCulture("ru-Ru");
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.CreateSpecificCulture("ru-Ru");
            //stasHugeLib::HugeLib.LogClass.WriteToLog(100,  resourceManager.GetString("StartService"));
            stasHugeLib::HugeLib.LogClass.WriteToLog(100, $"Start CardRoute service ({Assembly.GetExecutingAssembly()?.GetName()?.Version})");
            Process proc = Process.GetCurrentProcess();
            if (proc.MainModule != null)
            {
                FileInfo fi = new FileInfo(proc.MainModule.FileName);
            }

            string xmlname =
                $"{System.AppDomain.CurrentDomain.BaseDirectory}{Assembly.GetEntryAssembly()?.GetName().Name}.xml";
            try
            {
                xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlname);
                xnm = new XmlNamespaceManager(xmlDoc.NameTable);
            }
            catch (Exception ex)
            {

                //stasHugeLib::HugeLib.LogClass.WriteToLog($"{resourceManager.GetString("ErrorSettingsLoad")}: {ex.Message}");
                stasHugeLib::HugeLib.LogClass.WriteToLog($"Setting file load error: {ex.Message}");
                return;
            }
            byte[] pwdbytes = stasHugeLib::HugeLib.Utils.AHex2Bin("62677BA11D876F70C0CFE788916D4561");
            pwdbytes[3] = (byte)82; 
            pwdbytes[11] = (byte)104;

            string pwd = stasHugeLib::HugeLib.Crypto.MyCrypto.TripleDES_DecryptData(
                stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Database/password", xnm), pwdbytes, CipherMode.ECB,
                PaddingMode.Zeros);
            while (pwd.EndsWith("00"))
                pwd = pwd.Substring(0, pwd.Length - 2);
            pwd = stasHugeLib::HugeLib.Utils.AHex2String(pwd);
            string server = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Database/server", xnm);
            string db = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Database/name", xnm);
            string uid = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Database/uid", xnm);


            connectionString = $"Server={server};Database={db};Uid={uid};Pwd={pwd};";

            factory = DbProviderFactories.GetFactory(stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Database/providerName", xnm));
            CdpClass.cdpExe = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Cdp/Console", xnm);
            CdpClass.iniFolder = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Cdp/IniFolder", xnm).Replace("..\\", CdpClass.cdpFolder);
            CdpClass.inFile = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Cdp/InFile", xnm).Replace("..\\", CdpClass.cdpFolder);
            CdpClass.iniName = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Cdp/CdpIni", xnm).Replace("..\\", CdpClass.iniFolder);

            try
            {
                timerInterval = Convert.ToInt32(stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Common/Timeout", xnm)) * 1000;
            }
            catch (Exception e)
            {
                timerInterval = 5000;
            }

            lang = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Common/Language", xnm).ToLower();
            if (lang != "russian" && lang != "english")
                lang = "russian";
            protocol = stasHugeLib::HugeLib.XmlClass.GetDataXml(xmlDoc, "Common/Protocol", xnm).ToLower();
            if (protocol != "http" && protocol != "https")
                protocol = "https";


            //хэш таблица для выпуска карт
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                }
                catch (Exception ex)
                {
                    //stasHugeLib::HugeLib.LogClass.WriteToLog($"{resourceManager.GetString("ErrorDbConnect")}: {ex.Message}");
                    stasHugeLib::HugeLib.LogClass.WriteToLog($"Database connection error: {ex.Message}");
                    return;
                }

                using (SqlCommand comm = conn.CreateCommand())
                {
                    comm.CommandText = "select DeviceId from Devices";
                    using (SqlDataReader dr = comm.ExecuteReader())
                    {
                        while (dr.Read())
                            htIssue.Add(Convert.ToInt32(dr["DeviceId"]), false);
                        dr.Close();
                    }
                }
                conn.Close();
            }

            timerStart = new Timer();
            timerStart.Interval = timerInterval;
            timerStart.Elapsed += TimerStart_Elapsed;

            timerCdp = new Timer();
            timerCdp.Interval = timerInterval;
            timerCdp.Elapsed += TimerCdp_Elapsed;

            timerIssue = new Timer();
            timerIssue.Interval = timerInterval;
            timerIssue.Elapsed += TimerIssue_Elapsed;

            timerReport = new Timer();
            timerReport.Interval = timerInterval;
            timerReport.Elapsed += TimerReport_Elapsed;

            timerCentral = new Timer();
            timerCentral.Interval = timerInterval;
            timerCentral.Elapsed += TimerCentral_Elapsed;

            timerPin = new Timer();
            timerPin.Interval = timerInterval;
            timerPin.Elapsed += TimerPin_Elapsed;
            
            timerStart.Start();
            timerCdp.Start();
            timerIssue.Start();
            timerReport.Start();
            timerCentral.Start();
            timerPin.Start();
        }

        private void TimerPin_Elapsed(object sender, ElapsedEventArgs e)
        {
            timerPin.Stop();
            Interlocked.Increment(ref threadCount);
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                List<Card> cards = new List<Card>();
                SqlCommand sel = conn.CreateCommand();
                sel.CommandText = $"select x.* from ( " +
                                  "select c.cardid, CardPriorityId, c.DeviceId, d.DeviceName, cd.CardData, p.Link, p.ProductName, " +
                                  "rank() over(partition by c.deviceid order by cardpriorityid desc, c.cardid) num " +
                                  "from cards c " +
                                  "inner join Products p on c.ProductId = p.ProductId " +
                                  "inner join CardsData cd on c.CardId = cd.CardId " +
                                  "inner join Devices d on c.DeviceId = d.DeviceId " + 
                                  "where c.CardStatusId = @status " +
                                  ") x where x.num = 1 order by CardPriorityId";
                sel.Parameters.Add("@status", SqlDbType.Int).Value = (int)CardStatus.PinWaiting;
                using (SqlDataReader dr = sel.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        cards.Add(new Card()
                        {
                            cardId = Convert.ToInt32(dr["cardid"]),
                            cardData = dr["CardData"].ToString().Trim(),
                            productLink = dr["Link"].ToString().Trim(),
                            deviceName = dr["DeviceName"].ToString().Trim()
                        });
                    }
                    dr.Close();
                }
                foreach (Card c in cards)
                {
                    XmlDocument chain = new XmlDocument();
                    string f =
                        $"{Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Chains")}\\{c.productLink}.xml";
                    try
                    {
                        chain.Load(f);
                    }
                    catch
                    {
                        c.message = "Ошибка загрузки файла цепочки продукта";
                        SetCardStatus(c, CardStatus.Error, conn);
                        stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                        continue;
                    }
                    SetCardStatus(c, CardStatus.PinProcess, conn);
                    string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Pin", "NextLink", xnm);
                    if (String.IsNullOrEmpty(next))
                        next = "Complete";
                    #region эмуляция
                    string emDuration = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Pin/Emulation", "Duration", "-1", xnm);
                    int emulationDuration = 5;
                    try
                    {
                        emulationDuration = Convert.ToInt32(emDuration);
                    }
                    catch { }
                    if (emulationDuration > 0)
                    {
                        string emChance = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Pin/Emulation", "ErrorChance", "0", xnm);
                        int emulationChance = 0;
                        try
                        {
                            emulationChance = Convert.ToInt32(emChance);
                        }
                        catch { }
                        Thread.Sleep(emulationDuration * 1000);
                        Random r = new Random((int)DateTime.Now.Ticks);
                        if (r.Next(100) < emulationChance)
                        {
                            c.message = "Ошибка при эмуляции печати пина";
                            SetCardStatus(c, CardStatus.Error, conn);
                        }
                        else
                        {
                            SetCardStatus(c, next, conn);
                        }
                        continue;
                    }
                    #endregion
                    string type = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Pin", "Type", xnm);

                    XmlDocument cardData = new XmlDocument();
                    cardData.LoadXml(c.cardData);
                    if (type.ToLower().Trim() == "kkb")
                    {
                        XmlDocument pinDictionary = new XmlDocument();
                        f = $"{System.AppDomain.CurrentDomain.BaseDirectory}\\PinDictionary.xml";
                        try
                        {
                            pinDictionary.Load(f);
                        }
                        catch
                        {
                            stasHugeLib::HugeLib.LogClass.WriteToLog("PinDictionary.xml load error");
                        }
                        string deviceField = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Pin", "DeviceField", xnm);
                        string deviceName = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field", "Name", deviceField, "Value", xnm);
                        string service_ip = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(pinDictionary, "Service", "DeviceNames", deviceName, "Ip", xnm);
                        string service_port = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(pinDictionary, "Service", "DeviceNames", deviceName, "Port", xnm);
                        if (!String.IsNullOrEmpty(service_ip) && !String.IsNullOrEmpty(service_port))
                        {
                            string xml = "<CW_XML_Interface direction = 'Request' sequence = '1'><METHOD name = 'HostPinPrint'><RequestorPCName>CardRoute</RequestorPCName><RequestorName>CardRouteService</RequestorName>##DATA##</METHOD></CW_XML_Interface>";
                            xml = xml.Replace((char)0x27, (char)0x22); //меняем апостроф на кавычки
                            string part = String.Format("<DATAITEM name={0}##FIELD##{0} type={0}string{0} encoding={0}none{0}>##DATA##</DATAITEM>", (char)0x22);
                            string pindata = "";
                            int pinfieldcnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(chain, "Pin/Field", xnm);
                            for (int i = 0; i < pinfieldcnt; i++)
                            {
                                XmlDocument x = stasHugeLib::HugeLib.XmlClass.GetXmlNode(chain, "Pin/Field", i, xnm);
                                string name = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Name", xnm);
                                string sendas = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "SendAs", xnm);
                                string value = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field", "Name", name,"Value", xnm);
                                string function = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Function", xnm);
                                if (!String.IsNullOrEmpty(function))
                                    value = ApplyFunction(value, x);
                                if (String.IsNullOrEmpty(sendas))
                                    sendas = name;
                                pindata += part.Replace("##FIELD##", sendas).Replace("##DATA##", value);
                            }
                            xml = xml.Replace("##DATA##", pindata);
                            stasHugeLib::HugeLib.LogClass.WriteToLog(xml);
                            try
                            {
                                string reply = SendTCP(xml, service_ip, Convert.ToInt32(service_port));
                                reply = reply.Substring(reply.IndexOf("<CW"));
                                string start = stasHugeLib::HugeLib.Utils.AHex2String("5C22");
                                string old = stasHugeLib::HugeLib.Utils.AHex2String("22");
                                start = String.Format("\"");
                                reply = reply.Replace(start, "'");
                                XmlDocument pinreply = new XmlDocument();
                                pinreply.LoadXml(reply);
                                string ret_code = stasHugeLib::HugeLib.XmlClass.GetDataXml(pinreply, "METHOD/ReturnCode", xnm);
                                if (ret_code != "0")
                                    throw new Exception($"{ret_code}");
                                SetCardStatus(c, next, conn);
                            }
                            catch(Exception ex)
                            {
                                c.message = $"Pin printing error: {ex.Message}";
                                SetCardStatus(c, CardStatus.Error, conn);
                            }
                        }
                        else
                            SetCardStatus(c, next, conn);
                    }
                    else
                    {
                        c.message = "Pin printing error: no service data";
                        SetCardStatus(c, CardStatus.Error, conn);
                    }
                }
                conn.Close();
            }
            Interlocked.Decrement(ref threadCount);
            if (!stopFlag)
                timerPin.Start();
        }
        //взята из старого проекта CWHubService2.0, поэтому используется as is
        private string SendTCP(string str, string ip, int port)
        {
            byte[] b1 = new byte[0x8000];
            byte[] b2 = new byte[0xFFFF];
            Array.Clear(b1, 0, 0x8000);
            Array.Clear(b2, 0, 0xFFFF);
            int i = 0;
            b1[0] = 0x01;
            b1[1] = 0x53; b1[2] = 0x54; b1[3] = 0x44; //STD
            b1[4] = 0x31;
            b1[5] = 0x11;
            i = 6;
            string slen = Convert.ToInt32(str.Length).ToString("X");
            Array.Copy(Encoding.Default.GetBytes(slen), 0, b1, i, slen.Length);
            i += slen.Length;
            b1[i++] = 0x02;
            Array.Copy(Encoding.Default.GetBytes(str), 0, b1, i, str.Length);
            i += str.Length;

            try
            {
                using (TcpClient tcp = new TcpClient(ip, port))
                {
                    using (NetworkStream ns = tcp.GetStream())
                    {
                        ns.Write(b1, 0, i);
                        b1 = new byte[0x800];
                        Array.Clear(b1, 0, 0x800);
                        int t = 0;
                        i = 0;
                        do
                        {
                            i = ns.Read(b1, 0, 0x800);
                            Array.Copy(b1, 0, b2, t, i);
                            t += i;
                            Thread.Sleep(50);
                        }
                        while (ns.DataAvailable);
                        str = Encoding.Default.GetString(b2, 0, t);
                        ns.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
            return str;
        }

        private void TimerCentral_Elapsed(object sender, ElapsedEventArgs e)
        {
            timerCentral.Stop();
            Interlocked.Increment(ref threadCount);
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                List<Card> cards = new List<Card>();
                SqlCommand sel = conn.CreateCommand();
                sel.CommandText = $"select x.* from ( " +
                                    "select c.cardid, CardPriorityId, c.DeviceId, cd.CardData, p.Link, p.ProductName, " +
                                    "rank() over(partition by c.deviceid order by cardpriorityid desc, c.cardid) num " +
                                    "from cards c " +
                                    "inner join Products p on c.ProductId = p.ProductId " +
                                    "inner join CardsData cd on c.CardId = cd.CardId " +
                                    "where c.CardStatusId = @status " +
                                    ") x where x.num = 1 order by CardPriorityId";
                sel.Parameters.Add("@status", SqlDbType.Int).Value = (int) CardStatus.Central;
                using (SqlDataReader dr = sel.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        cards.Add(new Card()
                        {
                            cardId = Convert.ToInt32(dr["cardid"]),
                            cardData = dr["CardData"].ToString().Trim(),
                            productLink = dr["Link"].ToString().Trim()
                        });
                    }

                    dr.Close();
                }

                foreach (Card c in cards)
                {
                    XmlDocument chain = new XmlDocument();
                    string f =
                        $"{Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Chains")}\\{c.productLink}.xml";
                    try
                    {
                        chain.Load(f);
                    }
                    catch
                    {
                        c.message = "Ошибка загрузки файла цепочки продукта";
                        SetCardStatus(c, CardStatus.Error, conn);
                        stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                        continue;
                    }

                    try
                    {
                        string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Central", "NextLink", xnm);
                        if (String.IsNullOrEmpty(next))
                            next = "Complete";

                        #region эмуляция

                        string emDuration =
                            stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Central/Emulation", "Duration", "-1",
                                xnm);
                        int emulationDuration = 5;
                        try
                        {
                            emulationDuration = Convert.ToInt32(emDuration);
                        }
                        catch
                        {
                        }

                        if (emulationDuration > 0)
                        {
                            string emChance = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Central/Emulation",
                                "ErrorChance", "0", xnm);
                            int emulationChance = 0;
                            try
                            {
                                emulationChance = Convert.ToInt32(emChance);
                            }
                            catch
                            {
                            }

                            Thread.Sleep(emulationDuration * 1000);
                            Random r = new Random((int) DateTime.Now.Ticks);
                            if (r.Next(100) < emulationChance)
                            {
                                c.message = "Ошибка при эмуляции выгрузки на центральный выпуск";
                                SetCardStatus(c, CardStatus.Error, conn);
                            }
                            else
                            {
                                SetCardStatus(c, next, conn);
                            }

                            continue;
                        }

                        #endregion

                        XmlDocument cardData = new XmlDocument();
                        cardData.LoadXml(c.cardData);

                        int cnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(chain, "Central/PersoFile", xnm);
                        bool sendtocdp = false;
                        for (int i = 0; i < cnt; i++)
                        {
                            XmlDocument onereport =
                                stasHugeLib::HugeLib.XmlClass.GetXmlNode(chain, "Central/PersoFile", i, xnm);
                            string filename =
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(onereport, "", "File", xnm);
                            string delimiter =
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(onereport, "", "Delimiter", xnm);
                            int fieldcnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(onereport, "Field", xnm);
                            string str = "";
                            for (int t = 0; t < fieldcnt; t++)
                            {
                                XmlDocument onefield =
                                    stasHugeLib::HugeLib.XmlClass.GetXmlNode(onereport, "Field", t, xnm);
                                string name = stasHugeLib::HugeLib.XmlClass.GetAttribute(onefield, "", "Name", xnm);
                                string len =
                                    stasHugeLib::HugeLib.XmlClass.GetAttribute(onefield, "", "Length", xnm);
                                string def =
                                    stasHugeLib::HugeLib.XmlClass.GetAttribute(onefield, "", "Default", xnm);
                                string val = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field",
                                    "Name",
                                    name, "Value", xnm);
                                string mustbe =
                                    stasHugeLib::HugeLib.XmlClass.GetAttribute(onefield, "", "Mandatory", xnm);
                                if (String.IsNullOrEmpty(val))
                                    val = def;
                                if (String.IsNullOrEmpty(val) && mustbe.ToLower() == "true"
                                ) // если какого-то поля нет переводим в подготовку данных
                                {
                                    sendtocdp = true;
                                    break;
                                }

                                int l = 0;
                                if (Int32.TryParse(len, out l))
                                    val = val.PadRight(l);
                                if (t > 0)
                                    str += delimiter;
                                str += val;
                            }

                            if (sendtocdp)
                                break;
                            using (StreamWriter sw = new StreamWriter(filename, true))
                            {
                                sw.BaseStream.Seek(0, SeekOrigin.End);
                                sw.WriteLine(str);
                                sw.Close();
                            }
                        }

                        if (sendtocdp)
                        {
                            stasHugeLib::HugeLib.XmlClass.SetXmlAttribute(cardData, "Field", "Name",
                                "NextIsCentral",
                                "Value", xnm, "true");
                            SetCardData(c, cardData.InnerXml, conn);
                            SetCardStatus(c, CardStatus.PrepWaiting, conn);
                        }
                        else
                            SetCardStatus(c, next, conn);
                    }
                    catch (Exception ex)
                    {
                        c.message = "Central error: " + ex.Message;
                        SetCardStatus(c, CardStatus.Error, conn);
                        stasHugeLib::HugeLib.LogClass.WriteToLog("Central error: " + ex.Message);
                    }
                }
                   
                conn?.Close();
            }
            Interlocked.Decrement(ref threadCount);
            if (!stopFlag)
                timerCentral.Start();
        }

        private void TimerStart_Elapsed(object sender, ElapsedEventArgs e)
        {
            timerStart.Stop();
            Interlocked.Increment(ref threadCount);
            bool wasProcess = false;
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    List<Card> cards = new List<Card>();
                    SqlCommand sel = conn.CreateCommand();
                    //sel.CommandText =
                    //    $"select x.* from (select c.cardid, CardPriorityId, c.DeviceId, cd.CardData, p.Link, p.ProductName, " +
                    //    "rank() over(partition by c.deviceid order by cardpriorityid desc, c.cardid) num " +
                    //    "from cards c " +
                    //    "inner join Products p on c.ProductId = p.ProductId " +
                    //    "inner join CardsData cd on c.CardId = cd.CardId " +
                    //    "where c.CardStatusId = @status) x where x.num=1";
                    sel.CommandText =
                        $"select c.cardid, p.Link, c.ProductId " +
                        "from cards c " +
                        "inner join Products p on c.ProductId = p.ProductId " +
                        "where c.CardStatusId = @status order by c.CardPriorityId desc";

                    sel.Parameters.Add("@status", SqlDbType.Int).Value = (int) CardStatus.Start;
                    using (SqlDataReader dr = sel.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            wasProcess = true;
                            cards.Add(new Card()
                            {
                                cardId = Convert.ToInt32(dr["cardid"]),
                                productId = Convert.ToInt32(dr["ProductId"]),
                                productLink = dr["Link"].ToString().Trim()
                            });
                        }
                        dr.Close();
                    }

                    foreach (Card c in cards)
                    {
                        XmlDocument chain = new XmlDocument();
                        string f =
                            $"{Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Chains")}\\{c.productLink}.xml";
                        try
                        {
                            chain.Load(f);
                            string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "", "FirstLink", xnm);
                            SetCardStatus(c, next, conn);
                        }
                        catch
                        {
                            c.message = "Ошибка загрузки файла цепочки продукта";
                            SetCardStatus(c, CardStatus.Error, conn);
                        }
                    }
                }
                catch
                {

                }
                finally
                {
                    conn?.Close();
                    timerStart.Interval = (wasProcess) ? 50 : timerInterval+1;
                    Interlocked.Decrement(ref threadCount);
                    if (!stopFlag)
                        timerStart.Start();
                }
            }
        }
        private void TimerReport_Elapsed(object sender, ElapsedEventArgs e)
        {
            timerReport.Stop();
            Interlocked.Increment(ref threadCount);
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    List<Card> cards = new List<Card>();
                    SqlCommand sel = conn.CreateCommand();
                    sel.CommandText = $"select x.* from ( " +
                                      "select c.cardid, CardPriorityId, c.DeviceId, cd.CardData, p.Link, p.ProductName, " +
                                      "rank() over(partition by c.deviceid order by cardpriorityid desc, c.cardid) num " +
                                      "from cards c " +
                                      "inner join Products p on c.ProductId = p.ProductId " +
                                      "inner join CardsData cd on c.CardId = cd.CardId " +
                                      "where c.CardStatusId = @status " +
                                      ") x where x.num = 1 order by CardPriorityId";
                    sel.Parameters.Add("@status", SqlDbType.Int).Value = (int) CardStatus.ReportWaiting;
                    using (SqlDataReader dr = sel.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            cards.Add(new Card()
                            {
                                cardId = Convert.ToInt32(dr["cardid"]),
                                cardData = dr["CardData"].ToString().Trim(),
                                productLink = dr["Link"].ToString().Trim()
                            });
                        }

                        dr.Close();
                    }
                    foreach(Card c in cards)
                    { 
                        SetCardStatus(c, CardStatus.ReportProcess, conn);
                        XmlDocument chain = new XmlDocument();
                        string f =
                            $"{Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Chains")}\\{c.productLink}.xml";
                        try
                        {
                            chain.Load(f);
                        }
                        catch
                        {
                            c.message = "Ошибка загрузки файла цепочки продукта";
                            SetCardStatus(c, CardStatus.Error, conn);
                            stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                            continue;
                        }

                        string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Report", "NextLink", xnm);

                        #region эмуляция

                        string emDuration =
                            stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Report/Emulation", "Duration", "-1",
                                xnm);
                        int emulationDuration = 5;
                        try
                        {
                            emulationDuration = Convert.ToInt32(emDuration);
                        }
                        catch
                        {
                        }

                        if (emulationDuration > 0)
                        {
                            string emChance = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Report/Emulation",
                                "ErrorChance", "0", xnm);
                            int emulationChance = 0;
                            try
                            {
                                emulationChance = Convert.ToInt32(emChance);
                            }
                            catch
                            {
                            }

                            Thread.Sleep(emulationDuration * 1000);
                            Random r = new Random((int) DateTime.Now.Ticks);
                            if (r.Next(100) < emulationChance)
                            {
                                c.message = "Ошибка при эмуляции отчета";
                                SetCardStatus(c, CardStatus.Error, conn);
                            }
                            else
                            {
                                SetCardStatus(c, next, conn);
                            }

                            continue;
                        }

                        #endregion

                        XmlDocument cardData = new XmlDocument();
                        cardData.LoadXml(c.cardData);

                        int cnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(chain, "Report/Report", xnm);
                        for (int i = 0; i < cnt; i++)
                        {
                            XmlDocument onereport =
                                stasHugeLib::HugeLib.XmlClass.GetXmlNode(chain, "Report/Report", i, xnm);
                            string filename =
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(onereport, "", "File", xnm);
                            string delimiter =
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(onereport, "", "Delimiter", xnm);
                            int fieldcnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(onereport, "Field", xnm);
                            string str = "";
                            for (int t = 0; t < fieldcnt; t++)
                            {
                                XmlDocument onefield =
                                    stasHugeLib::HugeLib.XmlClass.GetXmlNode(onereport, "Field", t, xnm);
                                string name = stasHugeLib::HugeLib.XmlClass.GetAttribute(onefield, "", "Name", xnm);
                                string len =
                                    stasHugeLib::HugeLib.XmlClass.GetAttribute(onefield, "", "Length", xnm);
                                string def =
                                    stasHugeLib::HugeLib.XmlClass.GetAttribute(onefield, "", "Default", xnm);
                                string val = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field",
                                    "Name", name, "Value", xnm);
                                if (String.IsNullOrEmpty(val))
                                    val = def;
                                int l = 0;
                                if (Int32.TryParse(len, out l))
                                    val = val.PadRight(l);
                                if (t > 0)
                                    str += delimiter;
                                str += val;
                            }

                            using (StreamWriter sw = new StreamWriter(filename, true))
                            {
                                sw.BaseStream.Seek(0, SeekOrigin.End);
                                sw.WriteLine(str);
                                sw.Close();
                            }
                        }
                        SetCardStatus(c, next, conn);
                        
                    }
                }
                catch (Exception ex)
                {
                    stasHugeLib::HugeLib.LogClass.WriteToLog("Report error: " + ex.Message);
                }
                finally
                {
                    conn?.Close();
                    Interlocked.Decrement(ref threadCount);
                    if (!stopFlag)
                        timerReport.Start();
                }
            }
        }
        private void TimerIssue_Elapsed(object sender, ElapsedEventArgs e)
        {
            timerIssue.Stop();
            Interlocked.Increment(ref threadCount);
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    List<Card> cards = new List<Card>();
                    SqlCommand sel = conn.CreateCommand();
                    sel.CommandText = $"select x.* from ( " +
                                      "select c.cardid, CardPriorityId, c.DeviceId, cd.CardData, c.ProductId, c.BranchId, c.LastStatusId, " +
                                      "p.Link as ProductLink, p.ProductName, d.Link as DeviceLink, " +
                                      "rank() over(partition by c.deviceid order by cardpriorityid desc, c.cardid) num " +
                                      "from cards c " +
                                      "inner join Products p on c.ProductId = p.ProductId " + 
                                      "inner join Devices d on c.DeviceId = d.DeviceId " +
                                      "inner join CardsData cd on c.CardId = cd.CardId " +
                                      "where c.CardStatusId = @status " +
                                      ") x where x.num = 1 order by CardPriorityId";
                    sel.Parameters.Add("@status", SqlDbType.Int).Value = (int) CardStatus.PrintWaiting;
                    using (SqlDataReader dr = sel.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            cards.Add(
                                new Card()
                                {
                                    cardId = Convert.ToInt32(dr["cardid"]),
                                    cardData = dr["CardData"].ToString().Trim(),
                                    productId = Convert.ToInt32(dr["ProductId"]),
                                    productLink = dr["ProductLink"].ToString().Trim(),
                                    deviceLink = dr["DeviceLink"].ToString().Trim(),
                                    deviceId = Convert.ToInt32(dr["DeviceId"]),
                                    branchid = Convert.ToInt32(dr["branchId"]),
                                    lastStatusId =  Convert.ToInt32(dr["LastStatusId"])
                                });
                        }

                        dr.Close();
                    }
                    foreach (Card c in cards)
                    {
                        stasHugeLib::HugeLib.LogClass.WriteToLog($"Issue step starting: CardId = {c.cardId}, ProductChain = {c.productLink}, Device = {c.deviceLink}");
                        // проверяем на требуемость подтверждения. Если надо, то переводим в ожидание подтверждения, если нет, то добавляем в массив на выпуск
                        XmlDocument chain = new XmlDocument();
                        string f =
                            $"{Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Chains")}\\{c.productLink}.xml";
                        try
                        {
                            chain.Load(f);
                        }
                        catch
                        {
                            c.message = "Ошибка загрузки файла цепочки продукта";
                            SetCardStatus(c, CardStatus.Error, conn);
                            //stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                            htIssue[c.deviceId] = false;
                            continue;
                        }
                        string needConfirm = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Issue", "NeedConfirm", "", xnm);
                        if (needConfirm.Equals("admin", StringComparison.OrdinalIgnoreCase) && c.lastStatusId != (int)CardStatus.AdminPending)
                        {
                            SetCardStatus(c, CardStatus.AdminPending, conn);
                            continue;
                        }
                        if (needConfirm.Equals("operator", StringComparison.OrdinalIgnoreCase) && c.lastStatusId != (int)CardStatus.OperatorPending)
                        {
                            SetCardStatus(c, CardStatus.OperatorPending, conn);
                            continue;
                        }

                        if (!htIssue.ContainsKey(c.deviceId)) // если устройство было добавлено после начала работы сервиса, его надо добавить в хэш таблицу
                            htIssue.Add(c.deviceId, false);
                        if ((bool)htIssue[c.deviceId]) // сейчас что-то на этом эмбоссере делается
                            continue;
                        htIssue[c.deviceId] = true;
                        SetCardStatus(c, CardStatus.PrintProcess, conn);
                        Thread issueThread = new Thread(new ParameterizedThreadStart(IssueCard));
                        issueThread.Start(c);
                    }
                }
                catch (Exception exc)
                {
                    stasHugeLib::HugeLib.LogClass.WriteToLog($"Issue error: {exc.ToString()}");
                }
                finally
                {
                    conn.Close();
                    Interlocked.Decrement(ref threadCount);
                    if (!stopFlag)
                        timerIssue.Start();
                }
            }
        }
        private async void IssueCard(object o)
        {
            Card c = (Card)o;
            Interlocked.Increment(ref threadCount);
            //находим из какого лотка брать карту
            List<int> hoppers = new List<int>();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlCommand comm = conn.CreateCommand())
                {
                    try
                    {
                        comm.CommandText =
                            $"select hopper from DeviceProducts where DeviceId={c.deviceId} and ProductId={c.productId}";
                        using (SqlDataReader dr = comm.ExecuteReader())
                        {
                            while (dr.Read())
                                hoppers.Add(Convert.ToInt32(dr["hopper"]));
                            dr.Close();
                        }

                        if (hoppers.Count == 0)
                            throw new Exception(resourceManager.GetString("ErrorProductDevice"));
                        comm.CommandText = $"select DeviceTypeId from Devices where DeviceId={c.deviceId}";
                        c.deviceType = Card.GetDeviceType(Convert.ToInt32(comm.ExecuteScalar()));
                        if (c.deviceType == DeviceType.None)
                            throw new Exception(resourceManager.GetString("ErrorDeviceUnknown"));
                    }
                    catch (Exception e)
                    {
                        c.message = $"{resourceManager.GetString("ErrorIssue")}: {e.Message}";
                        SetCardStatus(c, CardStatus.Error, conn);
                        //stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                        htIssue[c.deviceId] = false;
                        conn.Close();
                        Interlocked.Decrement(ref threadCount);
                        return;
                    }
                }

                //htIssue[c.deviceId] = true;
                XmlDocument chain = new XmlDocument();
                string f =
                    $"{Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Chains")}\\{c.productLink}.xml";
                try
                {
                    chain.Load(f);
                }
                catch
                {
                    c.message = "Ошибка загрузки файла цепочки продукта";
                    SetCardStatus(c, CardStatus.Error, conn);
                    //stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                    htIssue[c.deviceId] = false;
                    conn.Close();
                    Interlocked.Decrement(ref threadCount);
                    return;
                }

                #region Emulation

                string emDuration =
                    stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Issue/Emulation", "Duration", "-1", xnm);
                int emulationDuration = 5;
                try
                {
                    emulationDuration = Convert.ToInt32(emDuration);
                }
                catch
                {
                }

                if (emulationDuration > 0)
                {
                    string emChance =
                        stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Issue/Emulation", "ErrorChance", "0", xnm);
                    int emulationChance = 0;
                    try
                    {
                        emulationChance = Convert.ToInt32(emChance);
                    }
                    catch
                    {
                    }

                    Thread.Sleep(emulationDuration * 1000);
                    Random r = new Random((int) DateTime.Now.Ticks);
                    if (r.Next(100) < emulationChance)
                    {
                        c.message = "Ошибка при эмуляции печати карты";
                        SetCardStatus(c, CardStatus.Error, conn);
                    }
                    else
                    {
                        string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Issue", "NextLink", xnm);
                        SetCardStatus(c, next, conn);
                    }
                    htIssue[c.deviceId] = false;
                    Interlocked.Decrement(ref threadCount);
                    return;
                }

                #endregion

                XmlDocument cardData = new XmlDocument();
                cardData.LoadXml(c.cardData);

                int cnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(chain, "Issue/Step", xnm);
                // отталкиваются пока от класса принтера
                PrinterClass device = null;

                if (c.deviceType == DeviceType.CD800 || c.deviceType == DeviceType.CE840 ||
                    c.deviceType == DeviceType.CE870)
                {
                    device = new Dpcl() {HopperID = c.hopper};
                }

                if (device == null)
                {
                    htIssue[c.deviceId] = false;
                    Interlocked.Decrement(ref threadCount);
                    return;
                }

                bool needSaveData = false;
                device.printerName = c.deviceLink;
                bool realwork = true;
                try
                {
                    device.eventPassMessage += Device_eventPassMessage;
                    ((Dpcl) device).Https = (protocol == "https");
                    ((Dpcl) device).CardId = c.cardId;
                    if (!device.StartJob())
                        throw new Exception("startjob error");
                    // подбираем лоток
                    try
                    {
                        // поставил в try, потому что в старых прошивках (например КубаньКредит) такой команды нет и дает ошибку
                        if (realwork)
                            c.hopper = device.FindHopper(hoppers.ToArray());
                    }
                    catch
                    {
                        c.hopper = hoppers[0];
                    }

                    ((Dpcl) device).HopperID = c.hopper;
                    if (realwork)
                    {
                        if (!device.StartCard())
                            throw new Exception("startcard error");
                    }

                    bool needResume = false;
                    FeedType currentFeed = FeedType.NotDefine;
                    for (int i = 0; i < cnt; i++)
                    {
                        XmlDocument step = stasHugeLib::HugeLib.XmlClass.GetXmlNode(chain, "Issue/Step", i, xnm);
                        string tp = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Type", xnm);
                        if (tp == "MagRead")
                        {
                            stasHugeLib::HugeLib.LogClass.WriteToLog($"MagRead step starting: CardId = {c.cardId}");
                            currentFeed = FeedType.Magstripe;
                            string[] tracks = null;
                            if (realwork)
                                tracks = device.GetMagstripe();
                            else
                            {
                                tracks = new string[]
                                {
                                    "B8880000000824113^DMITRIENKO/IRINA^21022012323231303233303230343",
                                    "8880000000824113=21022012323231303233303230343", ""
                                };
                            }
                            int readlength = 0;
                            if (tracks == null)
                                throw new Exception("MagRead: no track data");
                            else
                            {
                                int t1 = (tracks.Length > 0 && tracks[0] != null) ? tracks[0].Length : 0;
                                int t2 = (tracks.Length > 1 && tracks[1] != null) ? tracks[1].Length : 0;
                                int t3 = (tracks.Length > 2 && tracks[2] != null) ? tracks[2].Length : 0;
                                readlength = t1 + t2 + t3;
                                stasHugeLib::HugeLib.LogClass.WriteToLog($"MagRead step complete: CardId = {c.cardId}, track1 {t1} chars, track2 {t2} chars, track3 {t3} chars");
                            }
                            string saveCardData = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "SaveData", xnm);
                            int fcnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(step, "MakeField", xnm);
                            for (int t = 0; t < fcnt && readlength > 0; t++)
                            {
                                XmlDocument makefield =
                                    stasHugeLib::HugeLib.XmlClass.GetXmlNode(step, "MakeField", t, xnm);
                                string fieldName =
                                    stasHugeLib::HugeLib.XmlClass.GetAttribute(makefield, "", "Name", xnm);
                                string res = "";
                                int mkcnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(makefield, "MakeField", xnm);
                                string saveToDb =
                                    stasHugeLib::HugeLib.XmlClass.GetAttribute(makefield, "", "SaveToDb", xnm);
                                if (mkcnt == 0)
                                    res = CombineMakeField(makefield, tracks);
                                else
                                {
                                    for (int y = 0; y < mkcnt; y++)
                                    {
                                        XmlDocument x =
                                            stasHugeLib::HugeLib.XmlClass.GetXmlNode(makefield, "MakeField", y, xnm);
                                        res += CombineMakeField(x, tracks);
                                    }
                                }
                                res = ApplyFunction(res, makefield, cardData);

                                stasHugeLib::HugeLib.XmlClass.SetXmlAttribute(cardData, "Field", "Name", fieldName, "Value", xnm, res);
                                needSaveData = (saveCardData.ToLower() == "complete");
                                if (!String.IsNullOrEmpty(saveToDb) && !String.IsNullOrEmpty(res))
                                {
                                    using (SqlCommand comm = conn.CreateCommand())
                                    {
                                        comm.CommandText =
                                            $"update cards set {saveToDb}='{res}' where CardId={c.cardId}";
                                        try
                                        {
                                            comm.ExecuteNonQuery();
                                        }
                                        catch (Exception uEx)
                                        {
                                            stasHugeLib::HugeLib.LogClass.WriteToLog(
                                                $"MagRead SaveToDb error: {uEx.Message}");
                                        }
                                    }
                                }
                            }
                            if (readlength > 0 && saveCardData.ToLower() == "read")
                                SetCardData(c, cardData.InnerXml, conn);

                            if (i + 1 == cnt)
                            {
                                if (realwork)
                                {
                                    device.ResumeCard();
                                    device.RemoveCard(ResultCard.GoodCard);
                                    device.EndCard();
                                    device.StopJob();
                                }
                            }

                            needResume = true;
                        }
                        if (tp == "WaitStatus")
                        {
                            
                            string set = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Set", xnm);
                            string good = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Good", xnm);
                            string bad = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Bad", xnm);
                            string timeout = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Timeout", xnm);
                            
                            stasHugeLib::HugeLib.LogClass.WriteToLog($"Wait step starting: CardId = {c.cardId}, waitfor = {good}");

                            CardStatus csSet = (CardStatus) Enum.Parse(typeof(CardStatus), set, false);
                            CardStatus csGood = (CardStatus) Enum.Parse(typeof(CardStatus), good, false);
                            CardStatus csBad = (CardStatus) Enum.Parse(typeof(CardStatus), bad, false);
                            int iTimeout = 60;
                            if (!Int32.TryParse(timeout, out iTimeout))
                                iTimeout = 60;
                            SetCardStatus(c, CardStatus.Pause, conn);
                            //CardStatus newStatus;

//                            bool res = System.Threading.SpinWait.SpinUntil(
                            //                              () => WaitForStatusChange(conn, c.cardId, csSet), TimeSpan.FromSeconds(iTimeout))
                            Task<CardStatus> taskWait =
                                AsyncWaitForStatusChange(conn, c.cardId, csSet, 5000, iTimeout * 1000);

                            //stasHugeLib::HugeLib.LogClass.WriteToLog($"before wait {csSet}");
                            CardStatus newStatus = await taskWait;
                            //stasHugeLib::HugeLib.LogClass.WriteToLog($"after wait {newStatus}");

                            stasHugeLib::HugeLib.LogClass.WriteToLog($"Wait step status changed: CardId = {c.cardId}, newstatus = {newStatus}");

                            //если новый статус плохой, то ошибку поднял сервис киосков, должен был записать ошибку, оставляем ее. Выкидывает карту, выходим из цикла шагов
                            if (newStatus == csBad)
                                throw new stasHugeLib::HugeLib.MyException();
                            // если статус изменила на неизвестный (не плохой, который обработался выше и не хороший), то выкидываем карту и сами ставим ошибку.
                            if (newStatus != csGood)
                                throw new Exception($"WaitStatus error: Unknown status {newStatus}");
                            //иначе шаг заканчивается
                        }
                        if (tp == "ChipRead")
                        {
                            string tag = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Tag", xnm);
                            stasHugeLib::HugeLib.LogClass.WriteToLog($"ChipRead step starting: CardId = {c.cardId}, Tag = {tag}");
                            if (realwork && currentFeed != FeedType.SmartFront)
                                device.FeedCard(FeedType.SmartFront);
                            currentFeed = FeedType.SmartFront;

//                            string fieldName = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Field", xnm);
                            //string data = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field", "Name",
                            //    fieldName, "Value", xnm);

                            Scpp.Scpp scpp = new Scpp.Scpp();
                            Dictionary<string, string> inputs = new Dictionary<string, string>();
                            Dictionary<string, string> outs = new Dictionary<string, string>();
                            inputs.Add("GET_DATA", tag);
                            inputs.Add("AID", stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Aid", xnm));
                            inputs.Add("HS_ADDR", stasHugeLib::HugeLib.XmlClass.GetAttribute(xmlDoc, "HS", "Ip", xnm));
                            inputs.Add("HS_PORT",
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(xmlDoc, "HS", "Port", "1600", xnm));
                            inputs.Add("LOG",
                                (stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Log", xnm).ToLower() == "on")
                                    ? "1"
                                    : "0");
                            inputs.Add("LOGDIR", Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "ScppLogs"));
                            inputs.Add("READER_TYPE", "DPCL");
                            string[] addr = device.printerName.Split(':');
                            inputs.Add("READER", addr[0]);
                            inputs.Add("READER_PORT", (addr.Length > 1) ? addr[1] : ((protocol == "https") ? "9111" : "9100"));
                            inputs.Add("PROT", (protocol == "https") ? "HTTPS" : "HTTP");
                            inputs.Add("SCARD_PROTOCOL",
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Protocol", "Contact", xnm)
                                    .ToUpper());
                            int res = -1;
                            try
                            {
                                if (realwork)
                                    res = scpp.Perso(inputs, outs);
                                else
                                {
                                    res = 0;
                                    if (tag == "5A")
                                        outs.Add("GET_DATA", "6382984362718463");
                                    if (tag == "5F24")
                                        outs.Add("GET_DATA", "220731");
                                }
                            }
                            catch (Exception e)
                            {
                                if (realwork)
                                {
                                    device.ResumeCard();
                                    device.RemoveCard(ResultCard.RejectCard);
                                    device.StopJob();
                                }

                                c.message = $"Ошибка персонализации: {e.Message}";
                                SetCardStatus(c, CardStatus.Error, conn);
                                stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                                htIssue[c.deviceId] = false;
                                conn.Close();
                                Interlocked.Decrement(ref threadCount);
                                return;
                            }

                            if (res == 0)
                            {
                                string val = "";
                                if (outs.ContainsKey("GET_DATA"))
                                    val = outs["GET_DATA"];

                                stasHugeLib::HugeLib.LogClass.WriteToLog($"ReadChip step complete: CardId = {c.cardId}, '{tag}' = {val}");

                                string saveCardData = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "SaveData", xnm);
                                int fcnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(step, "MakeField", xnm);
                                for (int t = 0; t < fcnt; t++)
                                {
                                    XmlDocument makefield =
                                        stasHugeLib::HugeLib.XmlClass.GetXmlNode(step, "MakeField", t, xnm);
                                    string fieldName =
                                        stasHugeLib::HugeLib.XmlClass.GetAttribute(makefield, "", "Name", xnm);
                                    string resString = "";
                                    int mkcnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(makefield, "MakeField", xnm);
                                    string saveToDb =
                                        stasHugeLib::HugeLib.XmlClass.GetAttribute(makefield, "", "SaveToDb", xnm);
                                    if (mkcnt == 0)
                                        resString = CombineMakeField(makefield, new string[] { val });
                                    else
                                    {
                                        for (int y = 0; y < mkcnt; y++)
                                        {
                                            XmlDocument x =
                                                stasHugeLib::HugeLib.XmlClass.GetXmlNode(makefield, "MakeField", y, xnm);
                                            resString += CombineMakeField(x, new string[] { val });
                                        }
                                    }
                                    resString = ApplyFunction(resString, makefield);
                                    stasHugeLib::HugeLib.XmlClass.SetXmlAttribute(cardData, "Field", "Name", fieldName, "Value", xnm, resString);
                                    needSaveData = (saveCardData.ToLower() == "complete");
                                    if (!String.IsNullOrEmpty(saveToDb))
                                    {
                                        using (SqlCommand comm = conn.CreateCommand())
                                        {
                                            comm.CommandText =
                                                $"update cards set {saveToDb}='{resString}' where CardId={c.cardId}";
                                            try
                                            {
                                                comm.ExecuteNonQuery();
                                            }
                                            catch (Exception uEx)
                                            {
                                                stasHugeLib::HugeLib.LogClass.WriteToLog(
                                                    $"ChipRead SaveToDb error: {uEx.Message}");
                                            }
                                        }
                                    }
                                }
                                if (saveCardData.ToLower() == "read")
                                    SetCardData(c, cardData.InnerXml, conn);


                                // у нас только чтение - последний шаг и карту надо выдать в хорошие
                                if (i + 1 == cnt)
                                {
                                    if (realwork)
                                    {
                                        device.ResumeCard();
                                        device.RemoveCard(ResultCard.GoodCard);
                                        device.EndCard();
                                        device.StopJob();
                                    }
                                }

                                needResume = true;
                            }
                            else
                            {
                                if (realwork)
                                {
                                    device.ResumeCard();
                                    device.RemoveCard(ResultCard.RejectCard);
                                    device.EndCard();
                                    device.StopJob();
                                }

                                c.message = $"{resourceManager.GetString("ErrorPerso")}: {res}";
                                SetCardStatus(c, CardStatus.Error, conn);
                                stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                                htIssue[c.deviceId] = false;
                                conn.Close();
                                Interlocked.Decrement(ref threadCount);
                                return;
                            }
                        }
                        if (tp == "Perso")
                        {
                            if (realwork && currentFeed != FeedType.SmartFront)
                                device.FeedCard(FeedType.SmartFront);
                            currentFeed = FeedType.SmartFront;

                            string fieldName = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Field", xnm);
                            string data = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field", "Name",
                                fieldName, "Value", xnm);

                            Scpp.Scpp scpp = new Scpp.Scpp();
                            Dictionary<string, string> inputs = new Dictionary<string, string>();
                            inputs.Add("DATA", data);
                            inputs.Add("SCRIPT", stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Script", xnm));
                            inputs.Add("HS_ADDR", stasHugeLib::HugeLib.XmlClass.GetAttribute(xmlDoc, "HS", "Ip", xnm));
                            inputs.Add("HS_PORT",
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(xmlDoc, "HS", "Port", "1600", xnm));
                            inputs.Add("LOG",
                                (stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Log", xnm).ToLower() == "on")
                                    ? "1"
                                    : "0");
                            inputs.Add("LOGDIR", Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "ScppLogs"));
                            inputs.Add("READER_TYPE", "DPCL");
                            string[] addr = device.printerName.Split(':');
                            inputs.Add("READER", addr[0]);
                            inputs.Add("READER_PORT", (addr.Length > 1) ? addr[1] : ((protocol == "https") ? "9111" : "9100"));
                            inputs.Add("PROT", (protocol == "https") ? "HTTPS" : "HTTP");
                            inputs.Add("SCARD_PROTOCOL",
                                stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Protocol", "Contact", xnm)
                                    .ToUpper());
                            int res = -1;
                            try
                            {
                                if (realwork)
                                    res = scpp.Perso(inputs, null);
                                else
                                    res = 0;
                            }
                            catch (Exception e)
                            {
                                if (realwork)
                                {
                                    device.ResumeCard();
                                    device.RemoveCard(ResultCard.RejectCard);
                                    device.StopJob();
                                }

                                c.message = $"Ошибка персонализации: {e.Message}";
                                SetCardStatus(c, CardStatus.Error, conn);
                                stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                                htIssue[c.deviceId] = false;
                                conn.Close();
                                Interlocked.Decrement(ref threadCount);
                                return;
                            }

                            if (res == 0)
                            {
                                // у нас только персонализации - последний шаг и карту надо выдать в хорошие
                                if (i + 1 == cnt)
                                {
                                    if (realwork)
                                    {
                                        device.ResumeCard();
                                        device.RemoveCard(ResultCard.GoodCard);
                                        device.EndCard();
                                        device.StopJob();
                                    }
                                }
                                needResume = true;
                            }
                            else
                            {
                                if (realwork)
                                {
                                    device.ResumeCard();
                                    device.RemoveCard(ResultCard.RejectCard);
                                    device.EndCard();
                                    device.StopJob();
                                }

                                c.message = $"{resourceManager.GetString("ErrorPerso")}: {res}";
                                SetCardStatus(c, CardStatus.Error, conn);
                                stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                                htIssue[c.deviceId] = false;
                                conn.Close();
                                Interlocked.Decrement(ref threadCount);
                                return;
                            }
                        }
                        if (tp == "Print")
                        {
                            if (needResume && realwork)
                                device.ResumeCard();
                            string design = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "", "Design", xnm);
                            design = $"{AppDomain.CurrentDomain.BaseDirectory}\\Designs\\{design}";

                            XmlSerializer ser = new XmlSerializer(typeof(ProcardWPF.Card));
                            ser.UnknownNode += new XmlNodeEventHandler(Ser_UnknownNode);
                            ser.UnknownAttribute += new XmlAttributeEventHandler(Ser_UnknownAttribute);

                            XmlReader xr = XmlReader.Create(design, new XmlReaderSettings {IgnoreWhitespace = false});
                            ProcardWPF.Card procard = (ProcardWPF.Card) ser.Deserialize(xr);
                            xr.Close();

                            ((Dpcl) device).ClearEmboss();
                            if (procard.device?.DeviceType == ProcardWPF.DeviceType.CE)
                                ((Dpcl) device).NoTopper = ((XPSPrinter) procard.device).NoTopper;
                            procard.SetMas(100);
                            procard.SetTopLeftForPrint();
                            foreach (ProcardWPF.DesignObject dsO in procard.objects)
                            {
                                if (dsO.OType == ProcardWPF.ObjectType.MagStripe)
                                {
                                    string mp1Name = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "MagstripeFields",
                                        "Track1", "Track1", xnm);
                                    string mp2Name = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "MagstripeFields",
                                        "Track2", "Track2", xnm);
                                    string mp3Name = stasHugeLib::HugeLib.XmlClass.GetAttribute(step, "MagstripeFields",
                                        "Track3", "Track3", xnm);
                                    string track1 = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field",
                                        "Name", mp1Name, "Value",
                                        xnm);
                                    string track2 = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field",
                                        "Name", mp2Name, "Value",
                                        xnm);
                                    string track3 = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field",
                                        "Name", mp3Name, "Value",
                                        xnm);

                                    device.SetMagstripe(new string[] {track1, track2, track3});
                                }
                                if (dsO.OType == ProcardWPF.ObjectType.EmbossText2)
                                {
                                    string textToEmboss = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData,
                                        "Field", "Name",
                                        ((EmbossText2) dsO).Name,
                                        "Value", xnm);
                                    string formattedText = ((EmbossText2) dsO).Shablon;
                                    int posInNew = formattedText.IndexOf('*');
                                    int posInOld = 0;
                                    while (posInNew >= 0)
                                    {
                                        if (posInOld < textToEmboss.Length)
                                            formattedText = formattedText.Remove(posInNew, 1)
                                                .Insert(posInNew, textToEmboss[posInOld++].ToString());
                                        else
                                            formattedText = formattedText.Remove(posInNew, 1);
                                        posInNew = formattedText.IndexOf('*');
                                    }

                                    if (posInOld > 0) //если мы хоть раз зашли в обработку по спецсимволу
                                        textToEmboss = formattedText;
                                    dsO.SetText(textToEmboss);
                                    ((Dpcl) device).AddEmboss(new EmbossString()
                                    {
                                        font = (int) ((EmbossText2) dsO).Font,
                                        text = textToEmboss,
                                        x = Convert.ToInt32(1000 * ((EmbossText2) dsO).X),
                                        y = Convert.ToInt32(1000 * ((EmbossText2) dsO).Y)
                                    });
                                }
                                if (dsO.OType == ObjectType.ImageField)
                                {
                                    if (dsO.InType == InTypes.Db)
                                        dsO.SetText(stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData,
                                        "Field", "Name",
                                        ((EmbossText2)dsO).Name,
                                        "Value", xnm));
                                    else
                                        dsO.SetText(dsO.InData);
                                }
                                
                            }

                            if (procard.HasImage())
                            {
                                Bitmap front = MakeImage(procard, cardData, SideType.Front);
                                Bitmap back = MakeImage(procard, cardData, SideType.Back);
                                ((Dpcl) device).SetImageForPrint(front, back);
                            }

                            try
                            {
                                if (realwork)
                                {
                                    device.PrintCard();
                                    device.EndCard();
                                }

                                device.StopJob();
                            }
                            catch (Exception e)
                            { 
                                if (realwork)
                                {
                                    device.RemoveCard(ResultCard.RejectCard);
                                }

                                device.StopJob();
                                c.message = $"{resourceManager.GetString("ErrorIssue")}: {e.Message}";
                                SetCardStatus(c, CardStatus.Error, conn);
                                stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                                htIssue[c.deviceId] = false;
                                conn.Close();
                                Interlocked.Decrement(ref threadCount);
                                return;
                            }
                        }
                    }

                    if (needSaveData)
                        SetCardData(c, cardData.InnerXml, conn);
                    string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Issue", "NextLink", xnm);
                    SetCardStatus(c, next, conn);
                    htIssue[c.deviceId] = false;
                }
                catch (stasHugeLib::HugeLib.MyException)
                {
                    if (realwork)
                    {
                        device.RemoveCard(ResultCard.RejectCard);
                        device.StopJob();
                    }
                }
                catch (Exception e)
                {
                    c.message = $"{resourceManager.GetString("ErrorIssue")}: {e.Message}";
                    try
                    {
                        if (realwork)
                        { 
                            device.RemoveCard(ResultCard.RejectCard);
                            device.StopJob();
                        }
                    }
                    catch (Exception ee)
                    {
                        c.message = $"{resourceManager.GetString("ErrorIssue")}: {e.Message}";
                    }

                    SetCardStatus(c, CardStatus.Error, conn);
                    //stasHugeLib::HugeLib.LogClass.WriteToLog(c.message);
                }
                finally
                {
                    htIssue[c.deviceId] = false;
                    Interlocked.Decrement(ref threadCount);
                    conn.Close();
                }
            }
        }

        private string ApplyFunction(string value, XmlDocument node, XmlDocument cardData = null)
        {
            string res = value;
            string function =
                stasHugeLib::HugeLib.XmlClass.GetAttribute(node, "", "Function", xnm).ToLower();
            if (function == "enc")
            {
                res = stasHugeLib::HugeLib.Crypto.MyCrypto.TripleDES_EncryptData(
                    stasHugeLib::HugeLib.Utils.String2AHex(value),
                    stasHugeLib::HugeLib.Utils.AHex2Bin("BCC702CDABFE201C46B61C494FF8B6B6"), CipherMode.ECB,
                    PaddingMode.Zeros);
                return res;
            }

            if (function == "substring")
            {
                string sStart = stasHugeLib::HugeLib.XmlClass.GetAttribute(node, "", "Start", xnm);
                string sLength = stasHugeLib::HugeLib.XmlClass.GetAttribute(node, "", "Length", xnm);

                int iStart = 0, iLength = 0;
                Int32.TryParse(sStart, out iStart);
                Int32.TryParse(sLength, out iLength);
                if (iStart < value.Length)
                {
                    if (iLength == 0)
                        return value.Substring(iStart);
                    if (iStart + iLength < value.Length)
                        return value.Substring(iStart, iLength);
                    else
                        return value.Substring(iStart);
                }
                return "";
            }
            if (function == "mschangename")
            {
                string newname = stasHugeLib::HugeLib.XmlClass.GetAttribute(node, "", "NewName", xnm);
                string[] parts = value.Split('^');
                if (parts.Length > 1)
                    parts[1] = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field", "Name", newname, "Value", xnm);
                return String.Join("^", parts);
            }
            return res;
        }
        private async Task<CardStatus> AsyncWaitForStatusChange(SqlConnection conn, int cardId, CardStatus currentCardStatus, int frequency = 1000, int timeout = -1)
        {
            CardStatus res = currentCardStatus;
            var waitTask = Task.Run(async () =>
            {
                while ((res = GetCurrentCardStatus(conn, cardId)) == currentCardStatus)
                {
                    await Task.Delay(frequency);
                }
            });
            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(timeout)))
                throw new Exception($"Wait step timeout error: CardId={cardId}");
            return res;
        }
        private CardStatus GetCurrentCardStatus(SqlConnection conn, int cardId)
        {
            using (SqlCommand comm = conn.CreateCommand())
            {
                comm.CommandText = $"select CardStatusId from Cards where CardId={cardId}";
                object obj = comm.ExecuteScalar();
                return (CardStatus)obj;
            }
        }
        private Bitmap MakeImage(ProcardWPF.Card procard, XmlDocument cardData, SideType side)
        {
            procard.SetMas(100);
            Bitmap res = null;
            int col = 0;
            Media.DrawingVisual dv = new Media.DrawingVisual();
            bool graphictopper = procard.device?.DeviceType == ProcardWPF.DeviceType.CE && ((XPSPrinter) procard.device).GraphicTopper;
            using (Media.DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Media.Brushes.White, new Media.Pen(Media.Brushes.White, 1), new Rect(0, 0, ProcardWPF.Card.ClientToScreen(ProcardWPF.Card.Width), ProcardWPF.Card.ClientToScreen(ProcardWPF.Card.Height)));
                for (int t = 0; t < procard.objects.Count; t++)
                {
                    string formattedText = "";
                    if (side != procard.objects[t].Side)
                        continue;
                    if (graphictopper && procard.objects[t].OType == ObjectType.EmbossText2)
                    {
                        if (((EmbossText2)procard.objects[t]).IsEmbossFont)
                        {
                            MyFont font = null;
                            double x1 = 11, y1 = 13, d = 22;
                            switch (((EmbossText2) procard.objects[t]).Font)
                            {
                                case EmbossFont.Farrington:
                                    font = new MyFont() {FontName = "Farrington-7B-Qiqi", FontSize = 12, FontWeight = FontWeights.Bold };
                                    x1 = 10.8; y1 = 14; d = 13.3;
                                    break;
                                case EmbossFont.Gothic:
                                    font = new MyFont() { FontName = "C35495 C134", FontSize = 12 }; 
                                    break;
                            }

                            double x = ProcardWPF.Card.ClientXToScreen(procard.objects[t].X) - x1;
                            double y = ProcardWPF.Card.ClientYToScreen(procard.objects[t].Y, side) - y1;
                            foreach (char c in procard.objects[t].Text)
                            {
                                dc.DrawText(new Media.FormattedText(c.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,  font?.GetTypeface(), font.FontSize * 96.0 / 72.0, Media.Brushes.Black),new System.Windows.Point(x,y));
                                x += d;
                            }
                            col++;
                        }
                    }
                    if (procard.objects[t].OType == ObjectType.TextField)
                    {
                        string textToPrint = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData,
                            "Field", "Name",
                            procard.objects[t].Name,
                            "Value", xnm);
                        if (procard.objects[t].OType == ObjectType.TextField)
                            formattedText = ((TextField) procard.objects[t]).Shablon;
                        int posInNew = formattedText.IndexOf('*');
                        int posInOld = 0;
                        while (posInNew >= 0)
                        {
                            if (posInOld < textToPrint.Length)
                                formattedText = formattedText.Remove(posInNew, 1)
                                    .Insert(posInNew, textToPrint[posInOld++].ToString());
                            else
                                formattedText = formattedText.Remove(posInNew, 1);
                            posInNew = formattedText.IndexOf('*');
                        }

                        if (posInOld > 0) //если мы хоть раз зашли в обработку по спецсимволу
                            textToPrint = formattedText;

                        // поддержку спецсимвола перенес в перегруженный метод для текстового поля (для полей эмбоссирования пока что оставил как было, перед SetText)
                        procard.objects[t].SetText(textToPrint);
                        procard.objects[t].Draw(dc, Regim.ToPrinter, false, 0);
                        col++;
                    }
                    if (procard.objects[t].OType == ObjectType.ImageField)
                    {
                        procard.objects[t].Draw(dc, Regim.ToPrinter, false, 0);
                    }
                }
            }
            if (col > 0)
            {
                procard.SetMas(300);
                RenderTargetBitmap bitmap = new RenderTargetBitmap(
                    ProcardWPF.Card.ClientToScreen(ProcardWPF.Card.Width),
                    ProcardWPF.Card.ClientToScreen(ProcardWPF.Card.Height), 300, 300, Media.PixelFormats.Default);

                bitmap.Render(dv);
                MemoryStream ms = new MemoryStream();
                
                    BitmapEncoder be = new BmpBitmapEncoder();
                    be.Frames.Add(BitmapFrame.Create(bitmap));
                    be.Save(ms);
                    res = new Bitmap(ms);
                    res.Save($"Test_{side}.png", ImageFormat.Png);
                
            }
            return res;
        }

        private void Device_eventPassMessage(MessageType messageType, string message)
        {
            if (messageType == MessageType.CompleteStep)
            {
                string[] sss = message.ToLower().Split(':');
                if (sss[0].Equals("dispense"))
                {
                    int cid = Convert.ToInt32(sss[1]);
                    using (SqlConnection conn = new SqlConnection(connectionString))
                    {
                        conn.Open();
                        SetCardStatus(new Card() { cardId = cid }, CardStatus.IssueDispensing, conn);
                        conn.Close();
                    }
                }
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="data">если мы составляем из дорожек, то здесь три дорожки, если просто данные - то в они будут в 0-м элементе</param>
        /// <returns></returns>
        private string CombineMakeField(XmlDocument x, string[] data)
        {
            string start = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "StartPos", "0", xnm);
            string length = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Length", "-1", xnm);
            string ntrack = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Track", xnm);
            string def = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Default", xnm);
            string res = "";
            int itrack = -1;
            if (Int32.TryParse(ntrack, out itrack) && itrack > 0 && itrack <= data.Length)
                res = data[itrack - 1];
            if (itrack == 0 && data.Length == 1)
                res = data[0];
            if (!String.IsNullOrEmpty(def))
                res = def;
            try
            {
                int ilen = -1;
                Int32.TryParse(length, out ilen);
                if (ilen >= 0)
                    res = res.Substring(Convert.ToInt32(start), ilen);
                else
                    res = res.Substring(Convert.ToInt32(start));
            }
            catch 
            {
            }
            return res;
        }
        private void SetCardStatus(Card c, CardStatus status, SqlConnection conn)
        {
            bool waitingStatus = false;
            switch (status)
            {
                case CardStatus.PrintWaiting:
                case CardStatus.ReportWaiting:
                case CardStatus.PrepWaiting:
                case CardStatus.OperatorPending:
                case CardStatus.AdminPending:
                case CardStatus.PinWaiting:
                case CardStatus.Pause: // паузу ставлю так, чтобы последний статус тоже в нее устанавливал, в движке пауза ставится только если это картомат и мы ждем пока Паша его изменит
                {
                    waitingStatus = true;
                    break;
                }
            }
                //SqlTransaction trans = conn.BeginTransaction();
            using (SqlCommand upd = conn.CreateCommand())
            {
                //upd.Transaction = trans;
                if (waitingStatus)
                {
                    upd.CommandText =
                        $"update Cards set CardStatusId=@newstatus, LastActionDateTime=@lasttime, Message=@message, LastStatusId=@newstatus where CardId=@cardid \n\r" +
                        "insert into LogJournal (LogTypeId, LogDateTime, LogMessage) values (@logtype, @lasttime, @logmessage) \n\r" +
                        "select @@identity";
                }
                else
                {
                    upd.CommandText =
                        $"update Cards set CardStatusId=@newstatus, LastActionDateTime=@lasttime, Message=@message where CardId=@cardid \n\r" +
                        "insert into LogJournal (LogTypeId, LogDateTime, LogMessage) values (@logtype, @lasttime, @logmessage) \n\r" +
                        "select @@identity";
                }
                    
                upd.Parameters.Add("@newstatus", SqlDbType.Int).Value = (int)status;
                upd.Parameters.Add("@lasttime", SqlDbType.DateTime).Value = DateTime.Now;
                if (String.IsNullOrEmpty(c.message))
                    upd.Parameters.Add("@message", SqlDbType.NVarChar, 255).Value = DBNull.Value;
                else
                    upd.Parameters.Add("@message", SqlDbType.NVarChar, 255).Value = c.message;
                upd.Parameters.Add("@cardid", SqlDbType.Int).Value = c.cardId;
                upd.Parameters.Add("@logtype", SqlDbType.Int).Value = 200 + (int)status;
                upd.Parameters.Add("@logmessage", SqlDbType.NVarChar, 1024).Value = $"{resourceManager.GetString("LogSetStatus")}: {GetStatusName(status)}";
                object obj = upd.ExecuteScalar();
                stasHugeLib::HugeLib.LogClass.WriteToLog($"StatusChange: CardId = {c.cardId}, Status = {status}, Message = {c.message}");
                int logid = Convert.ToInt32(obj);
                upd.Parameters.Clear();
                upd.CommandText = $"insert into LogCard (LogRecordId, CardId) values ({logid}, {c.cardId})\r\n";
                upd.ExecuteNonQuery();
                if (c.productId > 0)
                {
                    upd.CommandText = 
                        $"insert into LogProduct(LogRecordId, ProductId) values({ logid}, { c.productId})\r\n";
                    upd.ExecuteNonQuery();
                }
                if (c.deviceId > 0)
                {
                    upd.CommandText =
                        $"insert into LogDevice (LogRecordId, DeviceId) values ({logid}, {c.deviceId})\r\n";
                    upd.ExecuteNonQuery();
                }
                if (c.branchid > 0)
                {
                    upd.CommandText =
                        $"insert into LogBranch (LogRecordId, BranchId) values ({logid}, {c.branchid})\r\n";
                    upd.ExecuteNonQuery();
                }
            }
        }
        private void SetCardData(Card c, string data, SqlConnection conn)
        {
            using (SqlCommand upd = conn.CreateCommand())
            {
                upd.CommandText = $"update CardsData set CardData=@carddata where CardId=@cardid;";
                upd.Parameters.Add("@carddata", SqlDbType.NVarChar).Value = data;
                upd.Parameters.Add("@cardid", SqlDbType.Int).Value = c.cardId;
                upd.ExecuteNonQuery();
            }
        }
        private void SetCardStatus(Card c, string status, SqlConnection conn)
        {
            switch (status)
            {
                case "Complete":
                    SetCardStatus(c, CardStatus.Complete, conn);
                    break;
                case "Report":
                    SetCardStatus(c, CardStatus.ReportWaiting, conn);
                    break;
                case "Cdp":
                    SetCardStatus(c, CardStatus.PrepWaiting, conn);
                    break;
                case "Issue":
                    SetCardStatus(c, CardStatus.PrintWaiting, conn);
                    break;
                case "CentralComplete":
                    SetCardStatus(c, CardStatus.CentralComplete, conn);
                    break;
                case "Pin":
                    SetCardStatus(c, CardStatus.PinWaiting, conn);
                    break;
            }

        }
        private void Ser_UnknownNode(object sender, XmlNodeEventArgs e)
        {
            stasHugeLib::HugeLib.LogClass.WriteToLog("XmlOpen, Unknown node: {0} = {1}", e.Name, e.Text);
        }
        private void Ser_UnknownAttribute(object sender, XmlAttributeEventArgs e)
        {
            stasHugeLib::HugeLib.LogClass.WriteToLog("XmlOpen, Unknown attribute: {0} = {1}", e.Attr.Name, e.Attr.Value);
        }
        private void TimerCdp_Elapsed(object sender, ElapsedEventArgs e)
        {
            timerCdp.Stop();
            Interlocked.Increment(ref threadCount);
            bool wasProccess = false;
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    List<Card> cards = new List<Card>();
                    SqlCommand sel = conn.CreateCommand();
                    sel.CommandText = $"select x.* from ( " +
                                "select c.cardid, CardPriorityId, c.DeviceId, cd.CardData, p.Link, p.ProductName, " +
                                "rank() over(partition by c.deviceid order by cardpriorityid desc, c.cardid) num " +
                                "from cards c " +
                                "inner join Products p on c.ProductId = p.ProductId " + 
                                "inner join CardsData cd on c.CardId = cd.CardId " +
                                "where c.CardStatusId = @status " +
                                ") x where x.num = 1 order by cardpriorityid desc";
                    sel.Parameters.Add("@status", SqlDbType.Int).Value = CardStatus.PrepWaiting;

                    using (DbDataReader dr = sel.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            cards.Add(new Card()
                            {
                                cardId = Convert.ToInt32(dr["cardid"]),
                                cardData = dr["CardData"].ToString().Trim(),
                                productLink = dr["Link"].ToString().Trim()
                            });
                        }
                        dr.Close();
                    }

                    wasProccess = cards.Count > 0;
                    foreach (Card c in cards)
                    {
                        stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step starting: CardId = {c.cardId}, ProductChain = {c.productLink}");
                        XmlDocument chain = new XmlDocument();
                        string f =
                            $"{Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Chains")}\\{c.productLink}.xml";
                        try
                        {
                            chain.Load(f);
                        }
                        catch (Exception ex)
                        {
                            c.message = "Ошибка загрузки файла цепочки продукта";
                            SetCardStatus(c, CardStatus.Error, conn);
                            stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step error: CardId = {c.cardId}, ProductChain = {c.productLink}, Error = 'Chain load error {ex.Message}'");
                            continue;
                        }
                        SetCardStatus(c, CardStatus.PrepProcess, conn);
                        #region
                        string emDuration = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Cdp/Emulation", "Duration", "-1", xnm);
                        int emulationDuration = 5;
                        try
                        {
                            emulationDuration = Convert.ToInt32(emDuration);
                        }
                        catch {}
                        if (emulationDuration > 0)
                        {
                            stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step emulation: CardId = {c.cardId}, ProductChain = {c.productLink}");
                            string emChance = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Cdp/Emulation", "ErrorChance", "0", xnm);
                            int emulationChance = 0;
                            try
                            {
                                emulationChance = Convert.ToInt32(emChance);
                            }
                            catch { }
                            Thread.Sleep(emulationDuration * 1000);
                            Random r = new Random((int)DateTime.Now.Ticks);
                            if (r.Next(100) < emulationChance)
                            {
                                c.message = "Ошибка при эмуляции подготовки данных";
                                SetCardStatus(c, CardStatus.Error, conn);
                                stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step error: CardId = {c.cardId}, ProductChain = {c.productLink}, Error = 'Scheduled emulation error'");
                            }
                            else
                            {
                                string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Cdp", "NextLink", xnm);
                                SetCardStatus(c, next, conn);
                                stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread:000000} Cdp step complete: CardId = {c.cardId}, ProductChain = {c.productLink}, NextStep = {next}");
                            }
                            continue;
                        }
                        #endregion
                        XmlDocument cardData = new XmlDocument();
                        cardData.LoadXml(c.cardData);

                        string inFile = stasHugeLib::HugeLib.XmlClass.GetDataXml(chain, "Cdp/InFile", xnm)
                            .Replace("..\\", CdpClass.cdpFolder);
                        string iniName = stasHugeLib::HugeLib.XmlClass.GetDataXml(chain, "Cdp/CdpIni", xnm)
                            .Replace("..\\", CdpClass.iniFolder);

                        int cnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(chain, "Cdp/InputStream/Field", xnm);
                        string inputString = "";
                        string delimiter = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Cdp/InputStream", "Delimiter", xnm);
                        string nextIsCentral = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field", "Name", "NextIsCentral", "Value", xnm); 
                        for (int i = 0; i < cnt; i++)
                        {
                            XmlDocument x = stasHugeLib::HugeLib.XmlClass.GetXmlNode(chain, "Cdp/InputStream/Field", i, xnm);
                            string fieldName = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Name", xnm);
                            string fieldLength = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Length", xnm);
                            string fieldDefault = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Default", xnm);
                            string val = stasHugeLib::HugeLib.XmlClass.GetXmlAttribute(cardData, "Field", "Name", fieldName, "Value", xnm);
                            string inFormatDate = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "InFormat", xnm);
                            string outFormatDate = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "OutFormat", xnm);
                            if (String.IsNullOrEmpty(val))
                                val = fieldDefault;
                            if (inFormatDate.Length > 0)
                            {
                                try
                                {
                                    DateTime dt = DateTime.ParseExact(val, inFormatDate, CultureInfo.InvariantCulture);
                                    val = dt.ToString(outFormatDate);
                                }
                                catch (Exception ex)
                                { }
                            }
                            int len = 0;
                            if (Int32.TryParse(fieldLength, out len))
                                val = val.PadRight(len);
                            inputString += $"{val}{delimiter}";
                        }

                        string err = "", outdata = "", outpin = "";
                        bool cdpres = false;
                        try
                        {
                            stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step: CardId = {c.cardId}, ProductChain = {c.productLink}, IniFile = {iniName}");
                            cdpres = CdpClass.RunCdp(inputString, inFile, iniName, out outdata, out outpin, out err);
                            if (!cdpres)
                                throw new Exception(err);
                            // данные
                            delimiter = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Cdp/OutputStream", "Delimiter", xnm);
                            cnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(chain, "Cdp/OutputStream/Field", xnm);
                            string[] strs = null;
                            if (delimiter.Length > 0)
                                strs = outdata.Split(delimiter[0]);
                            else
                                strs = new string[] { outdata };
                            for (int i = 0; i < cnt; i++)
                            {
                                XmlDocument x = stasHugeLib::HugeLib.XmlClass.GetXmlNode(chain, "Cdp/OutputStream/Field", i, xnm);
                                string fieldName = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Name", xnm);
                                string fieldLength = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Length", xnm);
                                string fieldDefault = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Default", xnm);
                                stasHugeLib::HugeLib.XmlClass.SetXmlAttribute(cardData, "Field", "Name", fieldName, "Value", xnm, (strs.Length >= i) ? strs[i] : "");
                            }
                            if (!String.IsNullOrEmpty(outpin))
                            {
                                delimiter = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Cdp/OutputPinStream",
                                    "Delimiter", xnm);
                                cnt = stasHugeLib::HugeLib.XmlClass.GetXmlNodeCount(chain, "Cdp/OutputPinStream/Field",
                                    xnm);
                                strs = null;
                                if (delimiter.Length > 0)
                                    strs = outpin.Split(delimiter[0]);
                                else
                                    strs = new string[] {outpin};
                                for (int i = 0; i < cnt; i++)
                                {
                                    XmlDocument x =
                                        stasHugeLib::HugeLib.XmlClass.GetXmlNode(chain, "Cdp/OutputPinStream/Field", i,
                                            xnm);
                                    string fieldName = stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Name", xnm);
                                    string fieldLength =
                                        stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Length", xnm);
                                    string fieldDefault =
                                        stasHugeLib::HugeLib.XmlClass.GetAttribute(x, "", "Default", xnm);
                                    stasHugeLib::HugeLib.XmlClass.SetXmlAttribute(cardData, "Field", "Name", fieldName,
                                        "Value", xnm, (strs.Length >= i) ? strs[i] : "");
                                }
                            }
                            SetCardData(c, cardData.InnerXml, conn);
                            string next = stasHugeLib::HugeLib.XmlClass.GetAttribute(chain, "Cdp", "NextLink", xnm);
                            if (nextIsCentral.ToLower() == "true")
                            {
                                stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step complete: CardId = {c.cardId}, ProductChain = {c.productLink}, NextStep = Central");
                                SetCardStatus(c, CardStatus.Central, conn);
                            }
                            else
                            {
                                stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step complete: CardId = {c.cardId}, ProductChain = {c.productLink}, NextStep = {next}");
                                SetCardStatus(c, next, conn);
                            }
                        }
                        catch (Exception exception)
                        {
                            c.message = exception.ToString();
                            SetCardStatus(c, CardStatus.Error, conn);
                            stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp step error: CardId = {c.cardId}, ProductChain = {c.productLink}, Error = '{exception.Message}'");
                        }
                    }
                }
                catch (Exception exc)
                {
                    stasHugeLib::HugeLib.LogClass.WriteToLog($"{System.Threading.Thread.CurrentThread.ManagedThreadId:000000} Cdp proccess error: {exc.ToString()}");
                }
                finally
                {
                    conn.Close();
                    timerCdp.Interval = (wasProccess) ? 50 : timerInterval;
                    //stasHugeLib::HugeLib.LogClass.WriteToLog("cdp thread reset");
                    Interlocked.Decrement(ref threadCount);
                    if (!stopFlag)
                        timerCdp.Start();
                }
            }
        }
        protected override void OnStop()
        {
            //timerStart?.Stop();
            //timerCdp?.Stop();
            //timerIssue?.Stop();
            //timerReport?.Stop();
            //stasHugeLib::HugeLib.LogClass.WriteToLog(100, "Stop service...");
            stasHugeLib::HugeLib.LogClass.WriteToLog(100, "CardRoute service stopping...Wait for all thread terminating");
            stopFlag = true;
            int i = 0;
            while (Interlocked.Read(ref threadCount) > 0)
            {
                Thread.Sleep(100);
                if (i++ > 1800)
                {
                    stasHugeLib::HugeLib.LogClass.WriteToLog(100, "interlocked not completed...");
                    break;
                }
            }
            stasHugeLib::HugeLib.LogClass.WriteToLog(100, "CardRoute service stopped");
        }
        protected override void OnStart(string[] args)
        {
            Start();
        }

        private string GetStatusName(CardStatus status)
        {
            switch (status)
            {
                case CardStatus.Start:
                    return resourceManager.GetString("StatusStart");
                case CardStatus.PrepWaiting:
                    return resourceManager.GetString("StatusPrepWaiting");
                case CardStatus.PrepProcess:
                    return resourceManager.GetString("StatusPrepProcess");
                case CardStatus.PrintWaiting:
                    return resourceManager.GetString("StatusPrintWaiting");
                case CardStatus.PrintProcess:
                    return resourceManager.GetString("StatusPrintProcess");
                case CardStatus.ReportWaiting:
                    return resourceManager.GetString("StatusReportWaiting");
                case CardStatus.ReportProcess:
                    return resourceManager.GetString("StatusReportProcess");
                case CardStatus.OperatorPending:
                    return resourceManager.GetString("StatusOperatorPending");
                case CardStatus.AdminPending:
                    return resourceManager.GetString("StatusAdminPending");
                case CardStatus.Error:
                    return resourceManager.GetString("StatusError");
                case CardStatus.Complete:
                    return resourceManager.GetString("StatusComplete");
                case CardStatus.Central:
                    return resourceManager.GetString("StatusCentral");
            }
            return "";
        }
    }
}
