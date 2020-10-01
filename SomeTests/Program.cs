namespace DPCL2Test
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.ServiceModel;
    using System.ServiceModel.Channels;
    using System.Text;
    using System.Xml;

    using DPCL2ServiceReference;
    using System.Net.Sockets;

    /// <summary>
    /// Main object of the application.
    /// </summary>
    class Program
    {
        /// <summary>
        /// My client identifier
        /// </summary>
        private const string MyClientID = "PC-NAME";

        /// <summary>
        /// The DPCL2 web service client
        /// </summary>
        private static DPCL2PortTypeClient dpcl2Client;

        /// <summary>
        /// The current job identifier
        /// </summary>
        private static uint jobID = 100;
        
        private string _ip = "213.251.249.148";
        /// <summary>
        /// The last action identifier
        /// </summary>
        private static uint lastActionID = 0;

        /// <summary>
        /// The current condition marker
        /// </summary>
        private static uint currentConditionMarker;

        /// <summary>
        /// Main metho d of the application.
        /// </summary>
        /// 
        private static bool toEject = true;

        private const int _WIDTH = 1013;
        private const int _HEIGHT = 638;

       
        static void Main(string[] args)
        {
            try
            {
                //подключение к эмбоссеру по ip адресу 
                using (dpcl2Client = CreateDPCL2Client(args?[0], true, true, 30))
                {
                    GetData();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());

            }
            //Console.WriteLine("Press any key...");
            //Console.ReadKey();
        }

        /// <summary>
        /// Creates the DPCL2 client and configures the web service client.
        /// </summary>
        /// <param name="ipAddress">The IP address.</param>
        /// <param name="isHttp">If set to <c>true</c>, we communicate in HTTP, otherwise in HTTPS.</param>
        /// <param name="bypassLocalProxy">If set to <c>true</c>, we bypass local proxies.</param>
        /// <param name="timeout">The timeout, in seconds.</param>
        /// <returns>The web service client.</returns>
        private static DPCL2PortTypeClient CreateDPCL2Client(
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
                ReaderQuotas = {
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
            //var port = isHttp ? "54010" : "54020";
            var endPoint = new EndpointAddress($"{protocol}://{ipAddress}:{port}");

            if (!isHttp)
            {
                // Don't do that, here we're ignoring printer certificate validation...
                ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, policyError) => true;
            }

            return new DPCL2PortTypeClient(binding, endPoint);
        }



        /// <summary>
        /// Gets the last job identifier.
        /// </summary>
        /// <returns>The last job ID</returns>
        private static void GetData()
        {
            var status = dpcl2Client.DiscoverPrinter(
                new DiscoverPrinterInput
                {
                    includeActions = true
                });
            Console.WriteLine($"Model: {status.model}");

            var statusIn = new WaitForStatus2Input
            {
                includeConditions = true,
                includeCounters = false,
                includeJobQueue = true,
                includeNetworkAdapters = false,
                includeSensors = false,
                includeSettings = true,
                includeSupplies = false,
                includeTunnels = false,
                matchConditionClient = null,
                matchConditionJobId = 0
            };

            var statusOut = dpcl2Client.WaitForStatus2(statusIn);
            var v1 = statusOut?.status?.settingsGroup?.FirstOrDefault(x => x.name == "Factory");
            var v2 = v1?.module.FirstOrDefault(x => x.name == "D1")?.subsystem.FirstOrDefault(y => y.name == "Options");
            foreach (var vv in v2.element)
                Console.WriteLine($"{vv.name}: {vv.value}");
        }

        /// <summary>
        /// Starts a job on the printer.
        /// </summary>
        private static void StartJob()
        {
            var startJob2In = new StartJob2Input
            {
                client = MyClientID,
                jobId = jobID,
                settingsGroup = string.Empty,
                exceptionJob = false
            };

            dpcl2Client.StartJob3(startJob2In);
        }

        /// <summary>
        /// Ends the current job.
        /// </summary>
        private static void EndJob()
        {
            var endJobIn = new EndJobInput { client = MyClientID, jobId = jobID };
            dpcl2Client.EndJob(endJobIn);
        }

        /// <summary>
        /// Cancels the current job.
        /// </summary>
        private static void CancelJob()
        {
            var cancelJobIn = new CancelJobInput { client = MyClientID, jobId = jobID };
            dpcl2Client.CancelJob(cancelJobIn);
        }

        /// <summary>
        /// Submits data corresponding to an action to the printer.
        /// </summary>
        /// <param name="contentType">The content type of the data.</param>
        /// <param name="data">The data.</param>
        private static void SubmitData(string contentType, byte[] data)
        {
            var submitDataIn = new SubmitDataInput
            {
                client = MyClientID,
                jobId = jobID,
                actionId = lastActionID,
                dataId = 1
            };

            var attachment = new Attachment();
            submitDataIn.attachment = attachment;
            attachment.contentType = contentType;
            attachment.Item = data;

            dpcl2Client.SubmitData(submitDataIn);
        }

        private static void SubmitData(string contentType, byte[] data, Parameter[] parameters)
        {
            var submitDataIn = new SubmitDataInput
            {
                client = MyClientID,
                jobId = jobID,
                actionId = lastActionID,
                dataId = 1,
                parameter = parameters
            };

            var attachment = new Attachment();
            submitDataIn.attachment = attachment;
            attachment.contentType = contentType;
            attachment.Item = data;

            dpcl2Client.SubmitData(submitDataIn);
        }
        /// <summary>
        /// Waits for the job completion.
        /// </summary>
        private static void WaitForCompletion()
        {
            var input = new WaitForStatus2Input
            {
                maxSeconds = 20,
                matchConditionClient = MyClientID,
                matchConditionJobId = jobID,
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

            while (true)
            {
                input.minConditionMarker = currentConditionMarker;
                var output = dpcl2Client.WaitForStatus2(input);
                currentConditionMarker = output.nextConditionMarker; //0 582

                if (output.status.condition == null)
                {
                    continue;
                }

                foreach (var c in output.status.condition.Where(c => c.client == MyClientID))
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
                            throw new Exception($"Printer error code {c.code}");
                    }
                }
            }
        }

        private static void GetSupplies()
        {
            var input = new WaitForStatus2Input
            {
                maxSeconds = 20,
                matchConditionClient = MyClientID,
                matchConditionJobId = jobID,
                minConditionSeverity = ConditionSeverity.Notice,
                includeConditions = true,
                includeCounters = true,
                includeJobQueue = false,
                includeNetworkAdapters = false,
                includeSensors = true,
                includeSettings = true,
                includeSupplies = true,
                includeTunnels = false
            };

            input.minConditionMarker = currentConditionMarker;
            var output = dpcl2Client.WaitForStatus2(input);

            uint foilRemainingPercent = output.status.supply.FirstOrDefault(x => x.module == "Embosser-TopperFoil").percentRemain;
            Console.WriteLine("Foil remaining: " + foilRemainingPercent);
            //  output.status.supply.

        }
    }
}
