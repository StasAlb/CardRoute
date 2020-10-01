using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DeviceViewer
{
    
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Timers.Timer timer = new Timer();
        private DataSet ds = new DataSet();
        public MainWindow()
        {
            InitializeComponent();
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = "Server=Stas10;Database=CardRoute;Uid=sa;Pwd=123;";
                conn.Open();
                using (SqlCommand comm = conn.CreateCommand())
                {
                    comm.CommandText = $"select dd.Name as DeviceName, d.Link, b.Name as Place from devices d inner join devicetypes dd on d.DeviceTypeId=dd.DeviceTypeId "+
                                            "inner join Branchs b on d.BranchId = b.BranchId";
                    using (SqlDataAdapter adapter = new SqlDataAdapter(comm))
                    {
                        adapter.Fill(ds, "Table");
                    }
                }
                conn.Close();
            }

            ds.Tables[0].Columns.Add("State");

            this.Dispatcher.Invoke(new Action(delegate ()
            {
                dgDevices.DataContext = ds.Tables[0].DefaultView;
            }));

            timer.Interval = 1000;
            timer.AutoReset = false;
            timer.Elapsed += Timer_Elapsed;
            timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
            {
                int index = i;
                var t = Task.Factory.StartNew(delegate
                {
                    DpclDevice.Dpcl dpcl = new DpclDevice.Dpcl();
                    try
                    {
                        dpcl.printerName = ds.Tables[0].Rows[index]["Link"].ToString();
                        dpcl.StartJob();
                        ds.Tables[0].Rows[index]["State"] = dpcl.GetPrinterStatus().ToString();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        dpcl?.StopJob();
                    }
                });
                //t.Start();
            }
            timer.Start();
        }
    }
}