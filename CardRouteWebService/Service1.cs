using System;
using System.IO;
using System.ServiceModel;
using System.Data.SqlClient;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;
using Microsoft.SqlServer.Server;


namespace CwHubWebService
{
    public class CardKB : IBindingInterface
    {
        enum CardStatus : int
        {
            PrepWaiting = 1,
            PrepProcess = 2,
            PinWaiting = 3,
            PinProcess = 4,
            IssueWaiting = 5,
            IssueProcess = 6,
            ReportWaiting = 7,
            ReportProcess = 8,
            Error = 9,
            Complete = 10,
            Start = 12,
            Central = 13,
            OperatorPending = 14,
            AdminPending = 15,
            IssueDispensing = 16, //вылезла из киоска и ждет пока ее возьмут
            CentralComplete = 17
        }
        private  byte[] AHex2Bin(string str)
        {
            str = str.ToUpper();
            byte[] res = new byte[str.Length / 2];
            int i = 0, c1 = 0, c2 = 0;
            while (i < str.Length)
            {
                c1 = (Char.IsDigit(str, i)) ? str[i] - '0' : str[i] - 'A' + 10;
                if (i + 1 < str.Length)
                    c2 = (Char.IsDigit(str, i + 1)) ? str[i + 1] - '0' : str[i + 1] - 'A' + 10;
                else
                {
                    c2 = c1;
                    c1 = 0;
                }
                res[i / 2] = (byte)(c1 * 16 + c2);
                i += 2;
            }
            return res;
        }
        private string Bin2AHex(byte[] bytes)
        {
            if (bytes == null)
                return "";
            string str = "";
            foreach (byte b in bytes)
                str = String.Format("{0}{1:X2}", str, b);
            return str;
        }
        private string TripleDES_DecryptData(byte[] indata, byte[] tdesKey, CipherMode cm, PaddingMode pm)
        {
            TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
            byte[] iv = new byte[tdes.BlockSize / 8];
            for (int t = 0; t < tdes.BlockSize / 8; t++)
                iv[t] = 0;
            byte[] outdata = new byte[tdes.FeedbackSize];
            byte[] resdata = new byte[((indata.Length % 8) == 0) ? indata.Length : ((indata.Length / 8) + 1) * 8];
            tdes.Mode = cm;
            tdes.Padding = pm;
            ICryptoTransform ct = null;

            if (System.Security.Cryptography.TripleDES.IsWeakKey(tdesKey))
            {
                MethodInfo mi = tdes.GetType().GetMethod("_NewEncryptor", BindingFlags.NonPublic | BindingFlags.Instance);
                object[] par = { tdesKey, tdes.Mode, iv, tdes.FeedbackSize, 1 };
                ct = (ICryptoTransform)mi.Invoke(tdes, par);
            }
            else
                ct = tdes.CreateDecryptor(tdesKey, iv);
            int i = 0, o = 0;
            while ((i + tdes.BlockSize / 8) < indata.Length)
            {
                ct.TransformBlock(indata, i, tdes.BlockSize / 8, outdata, 0);
                Array.Copy(outdata, 0, resdata, o, tdes.BlockSize / 8);
                i += tdes.BlockSize / 8;
                o += tdes.BlockSize / 8;
            }
            byte[] temp = ct.TransformFinalBlock(indata, i, indata.Length - i);
            Array.Copy(temp, 0, resdata, o, temp.Length);
            tdes.Clear();
            return Bin2AHex(resdata);
        }


        private string GetDBPassword(string cryptopwd)
        {
            byte[] pwdbytes = AHex2Bin("62677BA11D876F70C0CFE788916D4561");
            pwdbytes[3] = (byte)82;
            pwdbytes[11] = (byte)104;
            string pwd = TripleDES_DecryptData(AHex2Bin(cryptopwd), pwdbytes, CipherMode.ECB,PaddingMode.Zeros);
            while (pwd.EndsWith("00"))
                pwd = pwd.Substring(0, pwd.Length - 2);
            return Encoding.ASCII.GetString(AHex2Bin(pwd));
        }
        public printResponse print(printRequest printRequest)
        {
            printResponse resp = new printResponse();
            resp.Status = 1;
            

                        string pwd = System.Configuration.ConfigurationManager.AppSettings["DbPwd"];
                        string log = System.Configuration.ConfigurationManager.AppSettings["LogFile"];

                        WriteToLog(1, @"c:\ostcard\service.txt", $"logname: {log}");
                        WriteToLog(1, @"c:\ostcard\service.txt", $"dbpwd: {pwd}");
                        WriteToLog(1, @"c:\ostcard\service.txt", $"{System.Configuration.ConfigurationManager.ConnectionStrings["CardRouteDb"].ConnectionString}");
                        WriteToLog(1, log, $"printRequest: {printRequest.Pan}, {printRequest.EmbossedName}, {printRequest.EmbAlias}, {printRequest.ProductId}");
                        using (SqlConnection conn = new SqlConnection())
                        {
                            conn.ConnectionString = $"{System.Configuration.ConfigurationManager.ConnectionStrings["CardRouteDb"].ConnectionString}Password={GetDBPassword(pwd)};";
                            try
                            {
                                conn.Open();
                            }
                            catch (Exception ex)
                            {
                                WriteToLog(1, log, $"db error: {ex.Message}");
                                throw new FaultException<errorResponse>(new errorResponse() { ErrCode = (int)CardStatus.Error, ErrText = ex.Message });
                            }
                            using (SqlCommand comm = conn.CreateCommand())
                            {
                                comm.CommandText = "select CardId from Cards where pan=@pan";
                                comm.Parameters.AddWithValue("pan", printRequest.Pan + printRequest.SeqNum.ToString()); //в таблицу cards в пан записываем сразу с psn                    
                                object o = comm.ExecuteScalar();
                                if (o != null)
                                {
                                    comm.Parameters.Clear();
                                    //comm.Parameters.AddWithValue("@id", o.ToString());
                                    //comm.CommandText = "select count(*) from carddata where id=@id";
                                    //o = comm.ExecuteScalar();
                                    comm.CommandText = "update cards set CardStatusId=1, lastactiondatetime=getdate() where CardId="+Convert.ToInt32(o);

                                    resp.uIID = o.ToString();
                                    // resp.Status = (Convert.ToInt32(o) > 0) ? (int)CardStatus.WaitingPin : (int)CardStatus.WaitingProcessing;
                                    resp.Status = 1;
                                    //comm.Parameters["@status"].Value = resp.Status;
                                    comm.ExecuteNonQuery();
                                }
                                else
                                {
                                    using (SqlTransaction trans = conn.BeginTransaction())
                                    {
                                        try {
                                            comm.Transaction = trans;
                                            comm.CommandText = $"select DeviceId, DeviceTypeId from Devices where Alias='{printRequest.EmbAlias}'";
                                            SqlDataReader dr = comm.ExecuteReader();
                                            object deviceid = null, devicetype = null;

                                            if (dr.Read())
                                            {
                                                deviceid = dr["DeviceId"];
                                                devicetype = dr["DeviceTypeId"];
                                            }
                                            dr.Close();
                                            //object deviceid = comm.ExecuteScalar();
                                            if (deviceid == null || deviceid == DBNull.Value)
                                                throw new Exception($"Device {printRequest.EmbAlias} not found");
                                            comm.CommandText = $"select ProductId from Products where alias='{printRequest.ProductId}'";
                                            object productid = comm.ExecuteScalar();
                                            if (productid == null || productid == DBNull.Value)
                                                throw new Exception($"Product {printRequest.ProductId} not found");
                                            int newstatus = (Convert.ToInt32(devicetype) == 4) ? 13 : 1;
                                            comm.CommandText = $"insert into cards (pan, cardholder, jobid, productid, branchid, deviceid, cardstatusid, cardpriorityid, laststatusid, lastactiondatetime) " +
                                                $"values ('{printRequest.Pan + printRequest.SeqNum}', '{printRequest.EmbossedName}', 1, {Convert.ToInt32(productid)}, 1, {Convert.ToInt32(deviceid)}, {newstatus}, 1, {newstatus}, '{DateTime.Now.ToString("G")}') select @@identity";
                                            object cardid = comm.ExecuteScalar();

                                            XmlDocument carddata = new XmlDocument();
                                            carddata.LoadXml("<Data></Data>");
                                            SetXmlAttribute(carddata, "Field", "Name", "PAN", "Value", null, printRequest.Pan);
                                            SetXmlAttribute(carddata, "Field", "Name", "seqNum", "Value", null, printRequest.SeqNum.ToString());
                                            SetXmlAttribute(carddata, "Field", "Name", "startDate", "Value", null, printRequest.StartDate.ToString("yyyy-MM-dd"));
                                            SetXmlAttribute(carddata, "Field", "Name", "expirationDate", "Value", null, printRequest.ExpDate.ToString("yyyy-MM-dd"));
                                            SetXmlAttribute(carddata, "Field", "Name", "embossedName", "Value", null, printRequest.EmbossedName);
                                            SetXmlAttribute(carddata, "Field", "Name", "companyName", "Value", null, printRequest.CompanyName);
                                            SetXmlAttribute(carddata, "Field", "Name", "firstName", "Value", null, printRequest.FirstName);
                                            SetXmlAttribute(carddata, "Field", "Name", "secondName", "Value", null, printRequest.SecondName);
                                            SetXmlAttribute(carddata, "Field", "Name", "division", "Value", null, printRequest.DivisionName);
                                            SetXmlAttribute(carddata, "Field", "Name", "PinIP", "Value", null, printRequest.PinIP);

                                            comm.CommandText = $"insert into CardsData (CardId, CardData) values ({Convert.ToInt32(cardid)}, '{carddata.InnerXml}')";
                                            comm.ExecuteNonQuery();

                                            trans.Commit();
                                            resp.uIID = cardid.ToString();
                                            resp.Status = 1;
                                            WriteToLog(1, log,$"print response: {resp.uIID}, {resp.Status}");
                                        }
                                        catch  (Exception e)
                                        {
                                            trans.Rollback();
                                            conn.Close();
                                            WriteToLog(1, log, $"command error: {e.Message}");
                                            throw new FaultException<errorResponse>(new errorResponse() { ErrCode = (int)0xff, ErrText = e.Message });
                                        }

                                    }
                                    //comm.CommandText = "insert into Cards (pan, ";
                                    //comm.Parameters.Clear();
                                    //comm.CommandType = System.Data.CommandType.StoredProcedure;
                                    //comm.Parameters.AddWithValue("pan", printRequest.Pan);
                                    //comm.Parameters.AddWithValue("firstname", printRequest.FirstName);
                                    //comm.Parameters.AddWithValue("secondname", printRequest.SecondName);
                                    //comm.Parameters.AddWithValue("embossname", printRequest.EmbossedName);
                                    //comm.Parameters.AddWithValue("company", printRequest.CompanyName);
                                    //comm.Parameters.AddWithValue("startDate", printRequest.StartDate);
                                    //comm.Parameters.AddWithValue("expiryDate", printRequest.ExpDate);
                                    //comm.Parameters.AddWithValue("product", printRequest.ProductId);
                                    //comm.Parameters.AddWithValue("seqnum", printRequest.SeqNum);
                                    //comm.Parameters.AddWithValue("embosser", printRequest.EmbAlias);
                                    //comm.Parameters.AddWithValue("pin", printRequest.PinIP);
                                    //comm.Parameters.AddWithValue("division", printRequest.DivisionName);
                                    //comm.Parameters.AddWithValue("status", (int)CardStatus.WaitingProcessing);
                                    //comm.Parameters.Add("uuid", MySqlDbType.VarChar, 32);
                                    //comm.Parameters["uuid"].Direction = System.Data.ParameterDirection.Output;
                                    //using (MySqlTransaction trans = conn.BeginTransaction())
                                    //{
                                    //    try
                                    //    {
                                    //        comm.ExecuteNonQuery();
                                    //        resp.uIID = (string)comm.Parameters["uuid"].Value;
                                    //        resp.Status = (int)CardStatus.WaitingProcessing;
                                    //        trans.Commit();
                                    //    }
                                    //    catch (Exception ex)
                                    //    {
                                    //        trans.Rollback();
                                    //        conn.Close();
                                    //        throw new FaultException<errorResponse>(new errorResponse() { ErrCode = (int)CardStatus.Error, ErrText = ex.Message });
                                    //    }
                                    //}
                                }
                            }
                            conn.Close();
                        }
            return resp;
        }

        public checkResponse check(checkRequest checkRequest)
        {
            string pwd = System.Configuration.ConfigurationManager.AppSettings["DbPwd"];
            string log = System.Configuration.ConfigurationManager.AppSettings["LogFile"];

            WriteToLog(1, log, $"check: {checkRequest.uIID}");
            checkResponse resp = new checkResponse();
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = $"{System.Configuration.ConfigurationManager.ConnectionStrings["CardRouteDb"].ConnectionString}Password={GetDBPassword(pwd)};";
                try
                {
                    conn.Open();
                }
                catch (Exception ex)
                {
                    WriteToLog(1, log, $"db error: {ex.Message}");
                    throw new FaultException<errorResponse>(new errorResponse() { ErrCode = (int)0xff, ErrText = ex.Message });                    
                }
                using (SqlCommand comm = conn.CreateCommand())
                {
                    comm.CommandText = "select CardStatusId from cards where CardId=@id";
                    comm.Parameters.AddWithValue("@id", checkRequest.uIID);
                    try
                    {
                        object o = comm.ExecuteScalar();
                        resp.Status = Convert.ToInt32(o);
                        // стараемся отправлять статусы в соотвествии с прошлой реализацией
                        switch (Convert.ToInt32(o))
                        {
                            case (int)CardStatus.PrepWaiting:
                                resp.Status = (int)0x01;
                                break;
                            case (int)CardStatus.PrepProcess:
                                resp.Status = (int)0x20;
                                break;
                            case (int)CardStatus.PinWaiting:
                                resp.Status = (int)0x30;
                                break;
                            case (int)CardStatus.PinProcess:
                                resp.Status = (int)0x31;
                                break;
                            case (int)CardStatus.IssueWaiting:
                                resp.Status = (int)0x40;
                                break;
                            case (int)CardStatus.IssueProcess:
                                resp.Status = (int)0x41;
                                break;
                            case (int)CardStatus.Error:
                                resp.Status = (int)0xff;
                                break;
                            case (int)CardStatus.Complete:
                                resp.Status = (int)0x80;
                                break;
                            case (int)CardStatus.ReportWaiting:
                            case (int)CardStatus.ReportProcess:
                            case (int)CardStatus.Start:
                            case (int)CardStatus.Central:
                            case (int)CardStatus.OperatorPending:
                            case (int)CardStatus.AdminPending:
                            case (int)CardStatus.IssueDispensing:
                            case (int)CardStatus.CentralComplete:
                                break;
                        }
                        WriteToLog(1, log, $"check response: {resp.Status}");
                    }
                    catch
                    {
                        WriteToLog(1, log, $"db error: 0xff");
                        conn.Close();
                        throw new FaultException<errorResponse>(new errorResponse() { ErrCode = (int)0xff, ErrText = "Не найдено" });                        
                    }
                }
                conn.Close();
            }
            return resp;
        }

        public closeResponse close(closeRequest closeRequest)
        {
            string pwd = System.Configuration.ConfigurationManager.AppSettings["DbPwd"];
            string log = System.Configuration.ConfigurationManager.AppSettings["LogFile"];
            closeResponse resp = new closeResponse();
            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = $"{System.Configuration.ConfigurationManager.ConnectionStrings["CardRouteDb"].ConnectionString}Password={GetDBPassword(pwd)};";
                try
                {
                    conn.Open();
                }
                catch (Exception ex)
                {
                    WriteToLog(1, log, $"db error: {ex.Message}");
                    throw new FaultException<errorResponse>(new errorResponse() { ErrCode = (int)CardStatus.Error, ErrText = ex.Message });
                    //resp.Status = (int)CardStatus.Error;
                    //return resp;
                }
                using (SqlCommand comm = conn.CreateCommand())
                {
                    comm.Parameters.AddWithValue("@id", closeRequest.uIID);
                    try
                    {
                        comm.CommandText = "delete from cardsdata where CardId=@id";
                        comm.ExecuteNonQuery();
                        comm.CommandText = "delete from cards where CardId=@id";
                        comm.ExecuteNonQuery();
                        resp.Status = 1;//
                        WriteToLog(1, log, $"close response: {resp.Status}");
                    }
                    catch (Exception ex)
                    {
                        WriteToLog(1, log, $"db error: {ex.Message}");
                        conn.Close();
                        throw new FaultException<errorResponse>(new errorResponse() { ErrCode = (int)CardStatus.Error, ErrText = ex.Message });
                    }
                }
                conn.Close();
            }
            return resp;
        }
        public static void SetXmlAttribute(XmlDocument xd, string address, string TagName, string TagValue, string AttributeName, XmlNamespaceManager xnm, string value)
        {
            try
            {
                bool found = false;
                XmlNodeList xnl = xd.DocumentElement?.SelectNodes(address, xnm);
                for (int i = 0; i < xnl?.Count; i++)
                {
                    if (xnl[i].Attributes[TagName].Value == TagValue)
                    {
                        //XmlNode xn = xnl[i].SelectSingleNode(ElementName);
                        try
                        {
                            xnl[i].Attributes[AttributeName].Value = value;
                            found = true;
                        }
                        catch
                        {
                            XmlAttribute xa = xd.CreateAttribute(AttributeName);
                            xa.Value = value;
                            xnl[i].Attributes.Append(xa);
                        }
                        return;
                    }
                }
                if (!found)
                {
                    XmlNode xn = xd.CreateNode(XmlNodeType.Element, address, "");
                    XmlAttribute xa1 = xd.CreateAttribute(TagName);
                    xa1.Value = TagValue;
                    xn?.Attributes?.Append(xa1);
                    XmlAttribute xa2 = xd.CreateAttribute(AttributeName);
                    xa2.Value = value;
                    xn?.Attributes?.Append(xa2);
                    xd?.DocumentElement?.AppendChild(xn);
                }
                return;
            }
            catch (Exception x)
            {
                return;
            }
        }
        public static void WriteToLog(int level, string filename,  string str, params object[] pars)
        {
            if (String.IsNullOrEmpty(filename))
                return;
            DateTime dt = DateTime.Now;
            string strLog = String.Format("{0:HH.mm.ss}:{1:000}\t", dt, dt.Millisecond);
            strLog += String.Format(str, pars);
            try
            {
                System.IO.StreamWriter sw = new System.IO.StreamWriter(filename, true, System.Text.Encoding.GetEncoding(1251));
                //sw.Write("{0:HH.mm.ss}:{1:000}\t", dt, dt.Millisecond);
                //sw.WriteLine(str, pars);
                sw.WriteLine(strLog);
                sw.Close();
            }
            catch (Exception ex)
            {

            }
        }
    }

}