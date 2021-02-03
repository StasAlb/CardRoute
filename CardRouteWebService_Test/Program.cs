using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel;
using CwHubWebService;

namespace WebServiceTest
{
    class Program
    {
        //https://www.codeproject.com/Articles/576820/Basic-Step-by-Step-WCF-WebService

        static void Main(string[] args)
        {
            using (ChannelFactory<IBindingInterface> ServiceProxy =
            new ChannelFactory<IBindingInterface>("BasicHttpBinding_IBindingInterface"))
            {
                ServiceProxy.Open();
                IBindingInterface service = ServiceProxy.CreateChannel();

                printRequest pRequest = new printRequest();
                //heckRequest pRequest = new checkRequest();
                for (int i = 0; i < 1; i++)
                {
                    pRequest.Pan = String.Format("11189838202343{0:D2}", i);
                    pRequest.FirstName = "Cardholder";
                    pRequest.SecondName = i.ToString();
                    pRequest.EmbossedName = String.Format("Cardholder {0}", i);
                    pRequest.CompanyName = "ostpack";
                    pRequest.ExpDate = Convert.ToDateTime("31.12.2019");
                    pRequest.StartDate = DateTime.Now;
                    pRequest.ProductId = "235";
                    pRequest.SeqNum = "0";
                    //pRequest.EmbAlias = "CB_br_01_001";
                    pRequest.EmbAlias = "DC450";
                    pRequest.PinIP = "127.0.0.1";
                    //pRequest.DivisionName = "000001";
                    //pRequest.uIID = i.ToString();
                    printResponse resp = null;
                    //checkResponse resp = null;
                    try
                    {
                        resp = service.print(pRequest);
                        //resp = service.check(pRequest);
                    }
                    catch (FaultException<errorResponse> e)
                    {
                        Console.WriteLine(e.Detail.ErrText);
                    }
                    if (resp != null)
                        //Console.WriteLine(String.Format("status {0}, uiid {1}", resp.Status, pRequest.uIID));
                        Console.WriteLine($"Print result: {resp.Status}, id = {resp.uIID}");
                }
                ServiceProxy.Close();
            }
            Console.ReadKey();
        }
    }
}
