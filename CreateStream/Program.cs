using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.IO;

namespace CreateStream
{
    class Program
    {
        static void Main(string[] args)
        {
            //reset id в таблице DBCC CHECKIDENT ('Cards', RESEED, 1)  
            // имена брал с https://www.briandunning.com/sample-data/
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = @"Server=ALBER-10\MSSQL_2019;Database=CardRoute;Uid=sa;Pwd=123;";
                conn.Open();
                int indexbank = 1; // 0 - iberia, 1 - albion
                string[] bankname = new[] {"iberia", "albion"};
                int[] bankid = new[] {1, 2};
                string[] bins = new string[] {"486878", "512321"};
                int[] product = new[] {1, 2};
                int[] branch = new[] {1, 6};
                int[] device = new[] {25, 30};

                //int deviceCnt = 10;
                Random r = new Random(DateTime.Now.Millisecond);
                List<string> names = new List<string>();
                using (StreamReader sr = new StreamReader(File.OpenRead("us-500.csv")))
                {
                    sr.ReadLine(); // читаем первую строку, в которой нет имени
                    while (sr.Peek() >= 0)
                    {
                        string str = sr.ReadLine();
                        names.Add(str?.Split(',')[0] + " " + str?.Split(',')[1]);
                    }
                    sr.Close();
                }

                int c = 0;
                Console.Write($"Record: {c:000}");
                SqlTransaction trans = conn.BeginTransaction();
                using (SqlCommand comm = conn.CreateCommand())
                {
                    try
                    {
                        comm.Transaction = trans;
                        comm.CommandText = "insert into Jobs (JobName, JobDate, BankId) values (@name, @dt, @bank) select @@identity";
                        comm.Parameters.Add("@name", SqlDbType.NVarChar, 255).Value =
                            $"{bankname[indexbank]}_job_{DateTime.Now:T}";
                        comm.Parameters.Add("@dt", SqlDbType.DateTime).Value = DateTime.Now;
                        comm.Parameters.Add("@bank", SqlDbType.Int).Value = bankid[indexbank];

                        int jobid = Convert.ToInt32(comm.ExecuteScalar());
                        comm.Parameters.Clear();

                        comm.CommandText =
                            $"declare @cardid int "+
                            $"insert into Cards (PAN, Cardholder, JobId, ProductId, BranchId, DeviceId, CardStatusId, CardPriorityId, LastStatusId, LastActionDateTime) values " +
                            $"(@pan, @cardholder, @job, @product, @branch, @device, @status, @priority, @status, @dt) select @cardid=@@identity insert into CardsData (CardId,CardData) values (@cardid, @data)";
                        comm.Parameters.Add("@pan", SqlDbType.NVarChar, 64);
                        comm.Parameters.Add("@cardholder", SqlDbType.NVarChar, 50);
                        comm.Parameters.Add("@job", SqlDbType.Int).Value = jobid;
                        comm.Parameters.Add("@data", SqlDbType.NVarChar);
                        comm.Parameters.Add("@product", SqlDbType.Int).Value = product[indexbank];
                        comm.Parameters.Add("@branch", SqlDbType.Int).Value = branch[indexbank];
                        comm.Parameters.Add("@device", SqlDbType.Int).Value = device[indexbank];
                        comm.Parameters.Add("@status", SqlDbType.Int).Value = 1;
                        comm.Parameters.Add("@priority", SqlDbType.Int).Value = 2;
                        comm.Parameters.Add("@dt", SqlDbType.DateTime).Value = DateTime.Now;

                        string xml =
                            "<?xml version='1.0' encoding='utf-16'?><Data><Field Name='Pan' Value='##PAN##'/>" +
                            "<Field Name='ExpDate' Value='##DATE##'/><Field Name='Cardholder' Value='##NAME##'/>" +
                            "<Field Name='ServiceCode' Value='201'/></Data>";
                        for (int i = 0; i < 20; i++)
                        {
                            int p = r.Next(1, 999999999);
                            string pan = $"{bins[indexbank]}{p:0000000000}";
                            string name = names[r.Next(names.Count)];
                            comm.Parameters["@pan"].Value = pan;
                            comm.Parameters["@cardholder"].Value = name;
                            comm.Parameters["@data"].Value = xml.Replace("##PAN##", pan).Replace("##DATE##", "2207")
                                .Replace("##NAME##", name);
                            comm.ExecuteNonQuery();
                            Console.Write("\b\b\b");
                            Console.Write($"{c:000}");
                        }
                        trans.Commit();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        trans.Rollback();
                        Console.ReadKey();
                    }
                }
                conn.Close();
            }
        }
    }
}
