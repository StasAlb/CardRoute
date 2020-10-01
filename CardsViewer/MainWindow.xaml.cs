using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Data;
using System.Data.SqlClient;
using System.Timers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using HugeLib;

namespace CardsViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Timers.Timer timer = new Timer();
        public MainWindow()
        {
            InitializeComponent();
            timer.Interval = 1000;
            timer.AutoReset = true;
            timer.Elapsed += Timer_Elapsed;
            timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            XmlDocument xmlDoc = null;
            string xmlname =
                $"{System.AppDomain.CurrentDomain.BaseDirectory}CardRoute.xml";
            try
            {
                xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlname);
            }
            catch (Exception ex)
            {
                return;
            }

            string connectionString =
                $"Server={XmlClass.GetDataXml(xmlDoc, "Database/server", null)};" +
                $"Database={XmlClass.GetDataXml(xmlDoc, "Database/name", null)};" +
                $"Uid={XmlClass.GetDataXml(xmlDoc, "Database/uid", null)};" +
                $"Pwd={XmlClass.GetDataXml(xmlDoc, "Database/password", null)};";

            DataSet ds = new DataSet();
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = connectionString;
                conn.Open();
                using (SqlCommand comm = conn.CreateCommand())
                {
                    comm.CommandText = "select c.*, cd.CardData from cards c inner join CardsData cd on c.CardId=cd.CardId";
                    using (SqlDataAdapter adapter = new SqlDataAdapter(comm))
                    {
                        adapter.Fill(ds, "Table");
                    }
                }
                conn.Close();
            }

            ds.Tables[0].Columns.Add("CardStatus");
            ds.Tables[0].Columns.Add("CardPriority");
            for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
            {
                ds.Tables[0].Rows[i]["CardStatus"] = GetStatus((int) ds.Tables[0].Rows[i]["CardStatusId"]);
                ds.Tables[0].Rows[i]["CardPriority"] = GetPriority((int)ds.Tables[0].Rows[i]["CardPriorityId"]);
            }

            this.Dispatcher.Invoke(new Action(delegate()
            {
                dgCards.DataContext = ds.Tables[0].DefaultView;
            }));
        }
        private string GetStatus(int status)
        {
            switch (status)
            {
                case (int)CardStatus.PrepWaiting:
                    return "Ожидание подготовки";
                case (int)CardStatus.PrepProcess:
                    return "Идет подготовка";
                case (int)CardStatus.IssueWaiting:
                    return "Ожидание печати";
                case (int)CardStatus.IssueProcess:
                    return "Идет печать";
                case (int)CardStatus.ReportWaiting:
                    return "Ожидание отчета";
                case (int)CardStatus.ReportProcess:
                    return "Идет отчет";
                case (int)CardStatus.Error:
                    return "Ошибка";
                case (int)CardStatus.Complete:
                    return "Завершено";
            }
            return "???";
        }

        private string GetPriority(int priority)
        {
            switch (priority)
            {
                case (int)CardPriority.Low:
                    return "Низкий";
                case (int)CardPriority.Normal:
                    return "Нормальный";
                case (int)CardPriority.High:
                    return "Высокий";
            }
            return "???";
        }
    }
    enum CardStatus : int
    {
        PrepWaiting = 1,
        PrepProcess = 2,
        IssueWaiting = 5,
        IssueProcess = 6,
        ReportWaiting = 7,
        ReportProcess = 8,
        Error = 9,
        Complete = 10
    }

    enum CardPriority : int
    {
        Low = 1,
        Normal = 2,
        High = 3
    }

}
