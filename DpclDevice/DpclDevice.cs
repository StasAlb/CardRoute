using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Devices;
using HugeLib;
using DPCL2Test.DPCL2ServiceReference;

namespace DpclDevice
{
    public class Dpcl : Devices.PrinterClass
    {
        private DPCL2PortTypeClient dpcl2Client;
        public int HopperID;
        private uint JobId;
        private string ClientId = "Card Route";
        private uint LastAction = 0;
        private bool isKiosk = false;
        List<EmbossString> embossData = new List<EmbossString>();
        public bool Https = false;
        private int cardId = 0; // id карты для возврата при поднятии события Dispense
        public int CardId
        {
            set { cardId = value; }
        }
        
        public override bool StartJob()
        {
            dpcl2Client = CreateDPCL2Client(printerName, !Https, false, 30);
            //проверяем что устройство доступно
            //return (GetPrinterStatus() == PrinterStatus.Ready);
            return true;
        }

        public override bool StopJob()
        {
            if (JobId > 0)
                WaitForCompletion();
            dpcl2Client?.Close();
            return true;
        }

        public override bool StartCard()
        {
            if (GetPrinterStatus() != PrinterStatus.Ready)
                throw new Exception("Printer not ready");
            JobId = GetNewJobID();
            var startJob2In = new StartJob2Input
            {
                client = ClientId,
                jobId = JobId,
                settingsGroup = string.Empty,
                exceptionJob = false
            };

            dpcl2Client.StartJob3(startJob2In);
            Pick((uint)HopperID);
            return (JobId > 0);
        }

        public override int FindHopper(int[] hoppers)
        {
            var input = new WaitForStatus2Input
            {
                maxSeconds = 20,
                matchConditionClient = ClientId,
                matchConditionJobId = JobId,
                minConditionSeverity = ConditionSeverity.Notice,
                includeSensors = true,
                includeSettings = true
            };
            var output = dpcl2Client.WaitForStatus2(input);
            string kioskSupport = output?.status?.settingsGroup?.FirstOrDefault(y => y.name == "Group01")?.module.FirstOrDefault(z => z.name == "D1")?.subsystem.FirstOrDefault(w => w.name == "Options")?.element.FirstOrDefault(u => u.name == "EmbossModuleKioskSupport")?.value;
            isKiosk = (kioskSupport.ToLower() == "enabled");
            foreach (int n in hoppers)
            {
                string val = output?.status?.sensor?.FirstOrDefault(x => x.name == $"Hopper-Card{n}Present")?.value;
                if (val == "1")
                {
                    HopperID = n;
                    return n;
                }
            }
            return -1;
        }
        public override bool EndCard()
        {
            EndJob();
            WaitForCompletion();
            return true;
        }
        public override bool ResumeCard()
        {
            dpcl2Client.RestartJob(new RestartJobInput() {client=ClientId, jobId = JobId});
            return true;
        }

        public override bool PrintCard()
        {
            try
            {
                
                if (!String.IsNullOrEmpty(magstripe[0]) || !String.IsNullOrEmpty(magstripe[1]) ||
                    !String.IsNullOrEmpty(magstripe[2]))
                {
                    LogClass.WriteToLog($"Encode t1: {magstripe[0]}, t2: {magstripe[1]}, t3: {magstripe[2]}");
                    EncodeMagstripe(2, magstripe[0].ToUpper(), magstripe[1], magstripe[2]);
                }

                if (embossData.Count > 0)
                    EmbossData(embossData, true);
                else
                    Eject();
            }
            catch (Exception e)
            {
                CancelJob();
                EndJob();
                throw new Exception($"card print error: {e.Message}");
            }
            return true;
        }

        public override bool RemoveCard(ResultCard resultCard)
        {
            if (resultCard == ResultCard.RejectCard)
                CancelJob();
            if (resultCard == ResultCard.GoodCard)
                Eject();
            return true;
        }

        public override void SetParams(params object[] pars)
        {
            throw new NotImplementedException();
        }

        public override bool FeedCard(ProcardWPF.FeedType feedType)
        {
            //Pick((uint)HopperID);
            if (feedType == ProcardWPF.FeedType.SmartFront)
                Park();
            return true;
        }

        private DPCL2PortTypeClient CreateDPCL2Client(
            string ipAddress,
            bool isHttp,
            bool bypassLocalProxy,
            int timeout)
        {
            var textEncoding = new TextMessageEncodingBindingElement
            {
                MessageVersion = MessageVersion.Soap12,
                MaxReadPoolSize = 16384,
                MaxWritePoolSize = 16,
                WriteEncoding = Encoding.UTF8,
                ReaderQuotas =
                {
                    MaxDepth = 32,
                    MaxStringContentLength = 8192,
                    MaxBytesPerRead = 4096,
                    MaxNameTableCharCount = 16384
                }
            };

            var transport = isHttp ? new HttpTransportBindingElement() : new HttpsTransportBindingElement();
            transport.HostNameComparisonMode = HostNameComparisonMode.StrongWildcard;
            transport.AuthenticationScheme = AuthenticationSchemes.Anonymous;
            transport.KeepAliveEnabled = true;
            transport.MaxReceivedMessageSize = 1048576;
            transport.MaxBufferPoolSize = 1048576;
            transport.MaxBufferSize = 1048576;
            transport.ManualAddressing = false;
            transport.AllowCookies = false;
            transport.BypassProxyOnLocal = bypassLocalProxy;
            transport.ProxyAuthenticationScheme = AuthenticationSchemes.Anonymous;
            transport.Realm = string.Empty;
            transport.TransferMode = TransferMode.Buffered;
            transport.UnsafeConnectionNtlmAuthentication = false;
            transport.UseDefaultWebProxy = !bypassLocalProxy;

            var binding = new CustomBinding(textEncoding, transport)
            {
                OpenTimeout = new TimeSpan(0, 0, timeout),
                CloseTimeout = new TimeSpan(0, 0, timeout),
                ReceiveTimeout = new TimeSpan(0, 0, timeout),
                SendTimeout = new TimeSpan(0, 0, timeout)
            };

            var protocol = isHttp ? "http" : "https";
            var port = isHttp ? "9100" : "9111";
            //если порт не указан в адресе, то берем стандартные для http или https
            if (ipAddress.IndexOf(':') == -1)
                ipAddress = $"{ipAddress}:{port}";
            
            var endPoint = new EndpointAddress($"{protocol}://{ipAddress}");
            //var endPoint = new EndpointAddress($"{protocol}://{ipAddress}:{port}");

            if (!isHttp)
            {
                // Don't do that, here we're ignoring printer certificate validation...
                ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, policyError) => true;

            }

            return new DPCL2PortTypeClient(binding, endPoint);
        }

        public PrinterStatus GetPrinterStatus()
        {
            try
            {
                var status = dpcl2Client.WaitForStatus2(
                    new WaitForStatus2Input
                    {
                        includeConditions = false,
                        includeCounters = false,
                        includeJobQueue = false,
                        includeNetworkAdapters = false,
                        includeSettings = false,
                        includeSensors = false,
                        includeSupplies = false,
                        includeTunnels = false
                    });
                if (status.status.jobQueueState.Name == "Suspended")
                    return PrinterStatus.Suspended;
                if (status.status.state == PrinterState.Idle)
                    return PrinterStatus.Ready;
                return PrinterStatus.Busy;
            }
            catch (EndpointNotFoundException)
            {
                return PrinterStatus.Off;
            }
        }
        public void GetPrinterStatus2()
        {
            WaitForStatus2Output status = dpcl2Client.WaitForStatus2(
                new WaitForStatus2Input
                {
                    includeConditions = true,
                    includeCounters = true,
                    includeJobQueue = false,
                    includeNetworkAdapters = false,
                    includeSettings = true,
                    includeSensors = true,
                    includeSupplies = true,
                    includeTunnels = false
                });
            Debug.WriteLine($"Статус: {status?.status?.condition?.ToString()} {status?.status?.state}");
        }

        private uint GetNewJobID()
        {
            var statusIn = new WaitForStatus2Input
            {
                includeConditions = false,
                includeCounters = false,
                includeJobQueue = true,
                includeNetworkAdapters = false,
                includeSensors = false,
                includeSettings = false,
                includeSupplies = false,
                includeTunnels = false,
                matchConditionClient = null,
                matchConditionJobId = 0
            };

            var statusOut = dpcl2Client.WaitForStatus2(statusIn);
            if (statusOut.status.job == null)
            {
                return 100;
            }

            return statusOut.status.job
                       .Select(a => a.id)
                       .DefaultIfEmpty((uint) 100)
                       .Max() + 1;
        }
        /// <summary>
        /// Ends the current job.
        /// </summary>
        private void EndJob()
        {
            var endJobIn = new EndJobInput {client = ClientId, jobId = JobId};
            dpcl2Client.EndJob(endJobIn);
        }

        /// <summary>
        /// Cancels the current job.
        /// </summary>
        private void CancelJob()
        {
            var cancelJobIn = new CancelJobInput {client = ClientId, jobId = JobId};
            dpcl2Client.CancelJob(cancelJobIn);
        }
        

        /// <summary>
        /// Pick a card from the input hopper.
        /// </summary>
        /// <param name="inputHopper">The input hopper number.</param>
        private void Pick(uint inputHopper)
        {
            var parameters = new List<Parameter>();
            if (inputHopper == 0)
            {
                // Exception card
                parameters.Add(new Parameter {name = "InputHopperNumber", value = "Exception"});
            }
            else
            {
                // Input hopper is specified
                parameters.Add(new Parameter {name = "InputHopperNumber", value = inputHopper.ToString()});
            }

            // To perform any kind of action, we need to call the SubmitAction method
            SubmitAction("Pick", parameters.ToArray());
        }

        private void Park()
        {
            var parameters = new List<Parameter>();
            parameters.Add(new Parameter {name = "PageNumber", value = "1"});
            parameters.Add(new Parameter {name = "ParkPosition", value = "Smartcard"});

            // To perform any kind of action, we need to call the SubmitAction method
            SubmitAction("Park", parameters.ToArray());

            var input = new WaitForStatus2Input
            {
                maxSeconds = 20,
                matchConditionClient = ClientId,
                matchConditionJobId = JobId,
                minConditionSeverity = ConditionSeverity.Notice,
                includeConditions = true,
                includeCounters = false,
                includeJobQueue = false,
                includeNetworkAdapters = false,
                includeSensors = false,
                includeSettings = false,
                includeSupplies = false,
                includeTunnels = false
            };
            WaitForActionCompletion();
        }

        //private  void Smartcard()
        //{
        //    ExchangeMessageInput input = new ExchangeMessageInput();
        //    var parameters = new List<Parameter>();
        //    parameters.Add(new Parameter { name = "SCardProtocol", value = "SCARD_PROTOCOL_T0_OR_T1" });
        //    input.parameter = parameters.ToArray();
        //    input.tunnel = "SmartCard";
        //    input.type = "SCardConnect";
        //    input.maxSeconds = 5;
        //    ExchangeMessageOutput output = dpcl2Client.ExchangeMessage(input);

        //    input = new ExchangeMessageInput();
        //    input.tunnel = "SmartCard";
        //    input.type = "SCardStatus";
        //    input.maxSeconds = 5;
        //    output = dpcl2Client.ExchangeMessage(input);

        //    input = new ExchangeMessageInput();
        //    input.tunnel = "SmartCard";
        //    input.type = "SCardTransmit";
        //    input.maxSeconds = 5;
        //    MessageData md = new MessageData();
        //    md.ItemElementName = ItemChoiceType.base64;
        //    md.Item = AHex2Bin("80CA9F7F00");
        //    md.contentType = "application/vnd.dpcl.smartcard_buffer";
        //    input.data = md;
        //    output = dpcl2Client.ExchangeMessage(input);

        //    input = new ExchangeMessageInput();
        //    input.tunnel = "SmartCard";
        //    input.type = "SCardTransmit";
        //    input.maxSeconds = 5;
        //    md = new MessageData();
        //    md.ItemElementName = ItemChoiceType.base64;
        //    md.Item = AHex2Bin("80CA9F7F2D");
        //    md.contentType = "application/vnd.dpcl.smartcard_buffer";
        //    input.data = md;
        //    output = dpcl2Client.ExchangeMessage(input);
        //    Console.WriteLine($"smart response: {Bin2AHex((byte[])output.data.Item)}");
        //}
        //public  byte[] AHex2Bin(string str)
        //{
        //    str = str.ToUpper();
        //    byte[] res = new byte[str.Length / 2];
        //    int i = 0, c1 = 0, c2 = 0;
        //    while (i < str.Length)
        //    {
        //        c1 = (Char.IsDigit(str, i)) ? str[i] - '0' : str[i] - 'A' + 10;
        //        if (i + 1 < str.Length)
        //            c2 = (Char.IsDigit(str, i + 1)) ? str[i + 1] - '0' : str[i + 1] - 'A' + 10;
        //        else
        //        {
        //            c2 = c1;
        //            c1 = 0;
        //        }
        //        res[i / 2] = (byte)(c1 * 16 + c2);
        //        i += 2;
        //    }
        //    return res;
        //}
        //public  string Bin2AHex(byte[] bytes)
        //{
        //    if (bytes == null)
        //        return "";
        //    string str = "";
        //    foreach (byte b in bytes)
        //        str = String.Format("{0}{1:X2}", str, b);
        //    return str;
        //}

        /// <summary>
        /// Ejects the card.
        /// </summary>
        private void Eject()
        {
            SubmitAction("Eject", null);
        }

        /// <summary>
        /// Encodes the magstripe data.
        /// </summary>
        /// <param name="pageNumber">The page on which the magstripe is encoded (1 or 2, usually 2)</param>
        /// <param name="track1">Track 1 data.</param>
        /// <param name="track2">Track 2 data.</param>
        /// <param name="track3">Track 3 data.</param>
        private void EncodeMagstripe(int pageNumber, string track1, string track2, string track3)
        {
            SubmitAction("MagStripeEncode", new[] {new Parameter {name = "PageNumber", value = pageNumber.ToString()}});

            using (var str = new MemoryStream())
            {
                var doc = new XmlTextWriter(str, Encoding.UTF8);
                doc.WriteStartDocument();
                doc.WriteStartElement("magstripe");
                doc.WriteAttributeString("xmlns", "SOAP-ENV", null, "http://www.w3.org/2003/05/soap-envelope");
                doc.WriteAttributeString("xmlns", "SOAP-ENC", null, "http://www.w3.org/2003/05/soap-encoding");
                doc.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                doc.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
                doc.WriteAttributeString("xmlns", "DPCLMagStripe", null, "urn:dpcl:magstripe:2010-01-19");
                doc.WriteAttributeString("xsi", "type", null, "DPCLMagStripe:MagStripe");
                doc.WriteAttributeString("SOAP-ENV", "encodingStyle", null, "http://www.w3.org/2003/05/soap-encoding");

                if (track1 != null)
                {
                    doc.WriteStartElement("track");
                    doc.WriteAttributeString("number", "1");
                    doc.WriteElementString("stringData", track1);
                    doc.WriteEndElement();
                }

                if (track2 != null)
                {
                    doc.WriteStartElement("track");
                    doc.WriteAttributeString("number", "2");
                    doc.WriteElementString("stringData", track2);
                    doc.WriteEndElement();
                }

                if (track3 != null)
                {
                    doc.WriteStartElement("track");
                    doc.WriteAttributeString("number", "3");
                    doc.WriteElementString("stringData", track3);
                    doc.WriteEndElement();
                }

                doc.WriteEndElement();
                doc.WriteEndDocument();
                doc.Flush();

                SubmitData("application/vnd.dpcl.magstripe+xml", str.GetBuffer());
            }
        }

        public override string[] GetMagstripe()
        {
            string[] res = new string[3];
            SubmitAction("MagStripeRead", new[] { new Parameter { name = "PageNumber", value = "2" } });
            RetrieveDataOutput resp = dpcl2Client.RetrieveData(new RetrieveDataInput()
            {
                client = ClientId,
                jobId = JobId,
                actionId = LastAction
            });
            try
            {
                XmlDocument answer = new XmlDocument();
                answer.LoadXml(Encoding.Default.GetString((byte[]) resp.attachment.Item));
                for (int i = 0; i < 3; i++)
                {
                    XmlDocument x = XmlClass.GetXmlNode(answer, "track", "number", $"{i+1}", null);
                    try
                    {
                        res[i] = Encoding.Default.GetString(Convert.FromBase64String(XmlClass.GetTag(x, "base64Data", null)));
                    }
                    catch
                    {
                    }
                }
            }
            catch { }
            return res;
        }
        public override bool ReadMagstripe()
        {
            //SubmitAction("MagStripeRead", new[] {new Parameter {name = "PageNumber", value = "2"}});
            //RetrieveDataOutput resp = dpcl2Client.RetrieveData(new RetrieveDataInput()
            //{
            //    client = ClientId,
            //    jobId = JobId,
            //    actionId = LastAction
            //});
            return true;
        }

        /// <summary>
        /// Submits an action to the printer.
        /// </summary>
        /// <param name="action">The action name.</param>
        /// <param name="parameters">The parameters of the action.</param>
        private void SubmitAction(string action, Parameter[] parameters)
        {
            var submitActionIn = new SubmitActionInput
            {
                client = ClientId,
                jobId = JobId,
                actionId = ++LastAction,
                type = action,
                parameter = parameters

            };
            //Task<SubmitActionResponse> sa = dpcl2Client.SubmitActionAsync(submitActionIn);
            
            //var submitActionOut = sa.Result.output;
            var submitActionOut = dpcl2Client.SubmitAction(submitActionIn);
            var parametersStringBuilder = new StringBuilder();
            if (parameters != null && parameters.Length > 0)
            {
                parametersStringBuilder.Append('(');
                var isFirst = true;
                foreach (var p in parameters)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        parametersStringBuilder.Append(", ");
                    }

                    parametersStringBuilder.Append(p.name);
                    parametersStringBuilder.Append('=');
                    parametersStringBuilder.Append(p.value);
                }

                parametersStringBuilder.Append(')');
            }

            // If there is an error detected, we need to wait for the completion of the action
            if (!submitActionOut.success)
            {
                if (submitActionOut.checkStatus)
                {
                    WaitForCompletion();
                }
                else
                {
                    throw new Exception("Submit action failed unexpectedly");
                }
            }
        }

        /// <summary>
        /// Submits data corresponding to an action to the printer.
        /// </summary>
        /// <param name="contentType">The content type of the data.</param>
        /// <param name="data">The data.</param>
        private void SubmitData(string contentType, byte[] data)
        {
            var submitDataIn = new SubmitDataInput
            {
                client = ClientId,
                jobId = JobId,
                actionId = LastAction,
                dataId = 1
            };

            var attachment = new Attachment();
            submitDataIn.attachment = attachment;
            attachment.contentType = contentType;
            attachment.Item = data;

            dpcl2Client.SubmitDataAsync(submitDataIn);
        }

        public void ClearEmboss()
        {
            embossData.Clear();
        }

        public void AddEmboss(EmbossString embossString)
        {
            embossData.Add(embossString);
        }
        private void EmbossData(List<EmbossString> emboss_strings, Boolean TopperOn)
        {
            string actionName = (isKiosk) ? "EmbCardDataKiosk" : "EmbCardData";
            //применяется ли топпирование фольгой
            if (TopperOn)
                SubmitAction(actionName, new[] { new Parameter { name = "EmbossDataSource", value = "TopCard" } });
            else
               SubmitAction(actionName, null);

            using (var str = new MemoryStream())
            {
                var doc = new XmlTextWriter(str, Encoding.UTF8);
                doc.WriteStartDocument();
                doc.WriteStartElement("emboss");
                doc.WriteAttributeString("xmlns", "SOAP-ENV", null, "http://www.w3.org/2003/05/soap-envelope");
                doc.WriteAttributeString("xmlns", "SOAP-ENC", null, "http://www.w3.org/2003/05/soap-encoding");
                doc.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                doc.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
                doc.WriteAttributeString("xmlns", "DPCLEmboss", null, "urn:dpcl:emboss:2010-01-19");
                doc.WriteAttributeString("xsi", "type", null, "DPCLEmboss:Emboss");
                doc.WriteAttributeString("SOAP-ENV", "encodingStyle", null, "http://www.w3.org/2003/05/soap-encoding");

                foreach (EmbossString embs in emboss_strings)
                {
                    doc.WriteStartElement("line");
                    doc.WriteAttributeString("number", emboss_strings.IndexOf(embs).ToString());
                    doc.WriteElementString("font", embs.font.ToString());
                    doc.WriteElementString("horz", embs.x.ToString());
                    doc.WriteElementString("vert", embs.y.ToString());
                    doc.WriteElementString("stringData", embs.text);
                    doc.WriteEndElement();
                }

                doc.WriteEndElement();
                doc.WriteEndDocument();
                doc.Flush();

                SubmitData("application/vnd.dpcl.emboss+xml", str.GetBuffer());

                if (isKiosk)
                    SubmitAction("Dispense", null);
            }
        }


        /// <summary>
        /// Waits for the job completion.
        /// </summary>  
        private void WaitForCompletion()
        {
            var input = new WaitForStatus2Input
            {
                maxSeconds = 2,
                matchConditionClient = ClientId,
                matchConditionJobId = JobId,
                minConditionSeverity = ConditionSeverity.Notice,
                includeConditions = true,
                includeCounters = false,
                includeJobQueue = false,
                includeNetworkAdapters = false,
                includeSensors = false,
                includeSettings = false,
                includeSupplies = true,
                includeTunnels = false
            };

            uint currentConditionMarker = 0;
            uint? startConditionMarker = null;
            while (true)
            {
                input.minConditionMarker = currentConditionMarker;
                var output = dpcl2Client.WaitForStatus2(input);
                currentConditionMarker = output.nextConditionMarker;
                if (startConditionMarker == null)
                    startConditionMarker = currentConditionMarker;
                //LogClass.WriteToLog($"{currentConditionMarker}");
                if ((currentConditionMarker - startConditionMarker) >= 6)
                    SendMessage(MessageType.CompleteStep, $"dispense:{cardId}");

                if (output.status.condition == null)
                {
                    continue;
                }

                foreach (var c in output.status.condition.Where(c => c.client == ClientId))
                {
                    if ((c.code == 0) || (c.code == 1))
                    {
                        return;
                    }

                    switch (c.severity)
                    {
                        case ConditionSeverity.Notice:
                            break;
                        default:
                            throw new Exception(GetErrorMessage(c.code));
                    }
                }
            }
        }
        #warning может сделать одну WaitForCompletion, вроде отличаются только с.code
        private void WaitForActionCompletion()
        {
            var input = new WaitForStatus2Input
            {
                maxSeconds = 20,
                matchConditionClient = ClientId,
                matchConditionJobId = JobId,
                minConditionSeverity = ConditionSeverity.Notice,
                includeConditions = true,
                includeCounters = false,
                includeJobQueue = false,
                includeNetworkAdapters = false,
                includeSensors = false,
                includeSettings = false,
                includeSupplies = false,
                includeTunnels = false
            };

            int i = 0;
            uint currentConditionMarker = 0;
            while (true)
            {
                input.minConditionMarker = currentConditionMarker;
                var output = dpcl2Client.WaitForStatus2(input);
                currentConditionMarker = output.nextConditionMarker;

                if (output.status.condition == null)
                    continue;

                foreach (var c in output.status.condition.Where(c => c.client == ClientId))
                {
                    if (c.code == 6)
                        return;
                    switch (c.severity)
                    {
                        case ConditionSeverity.Notice:
                            break;
                        default:
                            throw new Exception($"Printer error code {c.code}");
                    }
                }
            }
        }
        private string GetErrorMessage(uint code)
        {
            switch (code)
            {
                case 100:
                    return "Request not supported";
                case 101:
                    return "Job could not complete";
                case 102:
                    return "Card not in position";
                case 103:
                    return "Printer problem";
                case 104:
                    return "Critical problem";
                case 105:
                    return "Magstripe data error";
                case 106:
                    return "Magstripe data not found";
                case 107:
                    return "Magstripe read data error";
                case 108:
                    return "Magstripe read no data";
                case 109:
                    return "Print ribbon problem";
                case 110:
                    return "Print ribbon is missing or out";
                case 111:
                    return "Card not picked";
                case 112:
                    return "Card hopper empty";
                case 113:
                    return "Close cover to continue";
                case 114:
                    return "Cover opened during job";
                case 116:
                    return "Magstripe not available";
                case 117:
                    return "Reader not available";
                case 118:
                    return "Print ribbon type problem";
                case 119:
                    return "Print ribbon not supported";
                case 195:
                    return "Card not taken? (195)";
                default:
                    return $"Printer error code {code}";
            }
        }
    }
}
