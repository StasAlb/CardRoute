using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.IO;
using System.Net.Configuration;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms.VisualStyles;
using System.Xml;
using CardRouteControl.ViewModel;
using HugeLib;
using HugeLib.Crypto;
using Microsoft.Win32;

namespace CardRouteControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        MySqlServer mySqlServer = new MySqlServer();
        MyCdp myCdp = new MyCdp();
        private MyCommon myCommon = new MyCommon();
        MyPerso myPerso = new MyPerso();

        private ServiceController controller = null;
        private string ServiceStatus;
        byte[] pwd = HugeLib.Utils.AHex2Bin("62677BA11D876F70C0CFE788916D4561");

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Command_ServiceStart(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                controller.Start();
                controller.Refresh();
                ServiceStatus = controller.Status.ToString();
                lStatus.Content = $"Статус: {ServiceStatus}";
                while (ServiceStatus == ServiceControllerStatus.StartPending.ToString())
                {
                    controller.Refresh();
                    ServiceStatus = controller.Status.ToString();
                    Thread.Sleep(100);
                }
                ServiceStatus = controller.Status.ToString();
                lStatus.Content = $"Статус: {ServiceStatus}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
            lStatus.Content = ServiceStatus;
            controller.Refresh();
            ServiceStatus = controller.Status.ToString();
            lStatus.Content = $"Статус: {ServiceStatus}";
        }

        private void Command_ServiceStartCanBeExecuted(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (controller?.Status.ToString() == "Stopped");
        }

        private void Command_ServiceStop(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                controller.Stop();
                controller.Refresh();
                ServiceStatus = controller.Status.ToString();
                lStatus.Content = $"Статус: {ServiceStatus}";
                while (ServiceStatus == ServiceControllerStatus.StopPending.ToString())
                {
                    controller.Refresh();
                    ServiceStatus = controller.Status.ToString();
                    Thread.Sleep(100);
                }
                ServiceStatus = controller.Status.ToString();
                lStatus.Content = $"Статус: {ServiceStatus}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        private void Command_ServiceStopCanBeExecuted(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (controller?.Status.ToString() == "Running");
        }

        public void SetLanguage(string lang)
        {
            switch (lang.ToLower())
            {
                case ("rus"):
                case ("russian"):
                case ("русский"):
                    SetLanguage(Lang.Russian);
                    break;
                case ("eng"):
                case ("english"):
                    SetLanguage(Lang.English);
                    break;
                default:
                    SetLanguage(Lang.Russian);
                    break;
            }
        }
        public void SetLanguage(Lang newLanguage)
        {
            ResourceDictionary dict = Application.Current.Resources;
            string resname = "Interface.ru.xaml";
            switch (newLanguage)
            {
                case Lang.Russian:
                    resname = "Interface.ru.xaml";
                    break;
                case Lang.English:
                    resname = "Interface.en.xaml";
                    break;
                default:
                    resname = "Interface.ru.xaml";
                    break;
            }
            try
            {
                dict.BeginInit();
                int i = 0;
                for (i = 0; i < dict.MergedDictionaries.Count; i++)
                {
                    if (((System.Windows.ResourceDictionary)dict.MergedDictionaries[i]).Source.LocalPath.EndsWith(resname))
                        break;
                }
                if (i < dict.MergedDictionaries.Count)
                {
                    ResourceDictionary res = dict.MergedDictionaries[i];
                    dict.MergedDictionaries.Remove(dict.MergedDictionaries[i]);
                    dict.MergedDictionaries.Add(res);
                }
            }
            finally
            {
                dict.EndInit();
            }
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            pwd[3] = (byte) 82;
            pwd[11] = (byte) 104;
            controller = new ServiceController();
            controller.ServiceName = "CardRoute";
            try
            {
                ServiceStatus = controller.Status.ToString();
            }
            catch
            {
                ServiceStatus = "не найден";
                controller = null;
            }
            
            SetLanguage(Lang.Russian);
            XmlDocument settings = new XmlDocument();
            try
            {
                settings.Load("CardRoute.xml");
                myCommon.timeout = XmlClass.GetDataXml(settings, "Common/Timeout", null);
                myCommon.language = XmlClass.GetDataXml(settings, "Common/Language", null);
                myCommon.protocol = XmlClass.GetDataXml(settings, "Common/Protocol", null);
                
                mySqlServer.serverName = XmlClass.GetDataXml(settings, "Database/server", null);
                mySqlServer.DbName = XmlClass.GetDataXml(settings, "Database/name", null);
                mySqlServer.Uid = XmlClass.GetDataXml(settings, "Database/uid", null);
                
                myCdp.CdpConsole = XmlClass.GetDataXml(settings, "Cdp/Console", null);
                myCdp.CdpIniFolder = XmlClass.GetDataXml(settings, "Cdp/IniFolder", null);
                myCdp.CdpDefaultIni = XmlClass.GetDataXml(settings, "Cdp/CdpIni", null);
                myCdp.CdpDefaultIn = XmlClass.GetDataXml(settings, "Cdp/InFile", null);

                myPerso.Ip = XmlClass.GetAttribute(settings, "HS", "Ip", "", null);
                myPerso.Port = XmlClass.GetAttribute(settings, "HS", "Port", "", null);
                myPerso.Log = XmlClass.GetAttribute(settings, "HS", "Log", "", null);

                //пароль последним, на случай ошибки
                mySqlServer.Pwd = Utils.AHex2String(MyCrypto.TripleDES_DecryptData(XmlClass.GetDataXml(settings, "Database/password", null), pwd, CipherMode.ECB, PaddingMode.Zeros));
            }
            catch
            { }
            tiCommon.DataContext = myCommon;
            tiSqlServer.DataContext = mySqlServer;
            tiCdp.DataContext = myCdp;
            tiPerso.DataContext = myPerso;
            lStatus.Content = $"Статус: {ServiceStatus}";

            tbPwd.Password = mySqlServer.Pwd;
        }

        private void bServerRefresh_OnClick(object sender, RoutedEventArgs e)
        {
            SqlDataSourceEnumerator sqlds = SqlDataSourceEnumerator.Instance;
            this.Cursor = Cursors.Wait;
            mySqlServer.ServerNames = SqlDataSourceEnumerator.Instance.GetDataSources();
            mySqlServer.ServerNames = SqlDataSourceEnumerator.Instance.GetDataSources();
            this.Cursor = Cursors.Arrow;
        }

        private void bTest_OnClick(object sender, RoutedEventArgs e)
        {
            this.Cursor = Cursors.Wait;
            SqlConnection conn = new SqlConnection();
            try
            {
                conn.ConnectionString =
                    $"Data Source={mySqlServer.serverName};Initial Catalog={mySqlServer.DbName};User Id={mySqlServer.Uid};Password={tbPwd.Password};Connection Timeout=30;";
                conn.Open();
                this.Cursor = Cursors.Arrow;
                MessageBox.Show((string)this.FindResource("DbConnectionOk"));
                conn.Close();
            }
            catch (Exception exc)
            {
                this.Cursor = Cursors.Arrow;
                MessageBox.Show(exc.Message);
            }
        }

        private void bCdpConsole_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog oFile = new OpenFileDialog();
            oFile.Filter = $"application files|*.exe|{(string)this.FindResource("AllFiles")}|*.*";
            if (oFile.ShowDialog() == true)
                myCdp.CdpConsole = oFile.FileName;
        }

        private void bCdpFolderIni_OnClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog selectDir = new System.Windows.Forms.FolderBrowserDialog();
            selectDir.RootFolder = Environment.SpecialFolder.Desktop;
            if (selectDir.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                myCdp.CdpIniFolder = selectDir.SelectedPath;
        }

        private void bCdpIniDefault_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog oFile = new OpenFileDialog();
            oFile.Filter = $"ini files|*.ini|{(string)this.FindResource("AllFiles")}|*.*";
            if (oFile.ShowDialog() == true)
                myCdp.CdpDefaultIni = oFile.FileName;
        }

        private void bCdpInDefault_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog oFile = new OpenFileDialog();
            oFile.Filter = $"txt files|*.txt|{(string)this.FindResource("AllFiles")}|*.*";
            if (oFile.ShowDialog() == true)
                myCdp.CdpDefaultIn = oFile.FileName;
        }

        private void bSave_OnClick(object sender, RoutedEventArgs e)
        {
            FileStream fs = new FileStream("CardRoute.xml", FileMode.Create);
            XmlTextWriter w = new XmlTextWriter(fs, Encoding.UTF8);
            w.Formatting = Formatting.Indented; 
            w.WriteStartDocument();
            w.WriteStartElement("Settings");
            w.WriteStartElement("Common");
            w.WriteElementString("Timeout", $"{myCommon.timeout}");
            w.WriteElementString("Language", $"{myCommon.language}");
            w.WriteElementString("Protocol", $"{myCommon.protocol}");
            w.WriteEndElement();
            w.WriteStartElement("Database");
            w.WriteElementString("providerName", $"System.Data.SqlClient");
            w.WriteElementString("server", $"{mySqlServer.serverName}");
            w.WriteElementString("name", $"{mySqlServer.DbName}");
            w.WriteElementString("uid", $"{mySqlServer.Uid}");
            w.WriteElementString("password", MyCrypto.TripleDES_EncryptData(Utils.String2AHex(tbPwd.Password), pwd, CipherMode.ECB, PaddingMode.Zeros));
            w.WriteEndElement();
            w.WriteStartElement("Cdp");
            w.WriteElementString("Console", $"{myCdp.CdpConsole}");
            w.WriteElementString("IniFolder", $"{myCdp.CdpIniFolder}");
            w.WriteElementString("InFile", $"{myCdp.CdpDefaultIn}");
            w.WriteElementString("CdpIni", $"{myCdp.CdpDefaultIni}");
            w.WriteEndElement();
            w.WriteStartElement("HS");
            w.WriteAttributeString("Ip", $"{myPerso.Ip}");
            w.WriteAttributeString("Port", $"{myPerso.Port}");
            w.WriteAttributeString("Log", $"{myPerso.Log}");
            w.WriteEndElement();
            w.WriteEndElement();
            w.Flush();
            fs.Close();
        }

        private void Command_ServiceRefresh(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                controller.Refresh();
                ServiceStatus = controller.Status.ToString();
            }
            catch
            {
                ServiceStatus = "не найден";
            }
            lStatus.Content = $"Статус: {ServiceStatus}";
        }
        private void cbLanguage_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var e1 = e.AddedItems?[0];
            SetLanguage(((ComboBoxItem)e1).Content.ToString());

        }
    }
    public enum Lang
    {
        Russian,
        English
    }

    public static class CustomCommands
    {
        public static readonly RoutedUICommand ServiceStart =
            new RoutedUICommand("ServiceStart", "ServiceStart", typeof(CustomCommands));
        public static readonly RoutedUICommand ServiceStop =
            new RoutedUICommand("ServiceStop", "ServiceStop", typeof(CustomCommands));
        public static readonly RoutedUICommand ServiceRefresh =
            new RoutedUICommand("ServiceRefresh", "ServiceRefresh", typeof(CustomCommands));

    }
}

