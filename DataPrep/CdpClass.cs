using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataPrep
{
    public static class CdpClass
    {
        public static string cdpExe;

        public static string cdpFolder
        {
            get
            {
                FileInfo fi = new FileInfo(cdpExe);
                return (fi.Exists) ? $"{fi.DirectoryName}\\" : "";
            }
        }
        public static string cdpError => Path.Combine(cdpFolder, "err.txt");
        public static string inFile;
        public static string iniFolder;
        public static string iniName;
        

        public static bool RunCdp(string data, string inF, string iniF, out string outdata, out string error)
        {
            error = "";
            outdata = "";
            
            using (StreamWriter sw = new StreamWriter((String.IsNullOrEmpty(inF)) ? inFile : inF, false,
                Encoding.GetEncoding(1251)))
            {
                sw.WriteLine(data);
                sw.Close();
            }

            string ini = String.IsNullOrEmpty(iniF) ? iniName : iniF;
            StringBuilder sb = new StringBuilder(255);
            HugeLib.IniFile.GetPrivateProfileString("Proekt", "OutFileName", "", sb, 255, ini);
            string outCdp = sb.ToString();

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = cdpExe;
            startInfo.Arguments = String.Format("{0}{1}{0}", (char)0x22, ini);

            File.Delete(cdpError);
            File.Delete(outCdp);
            using (Process pr = Process.Start(startInfo))
            {
                pr.WaitForExit();
                pr.Close();
                if (File.Exists(cdpError))
                {
                    using (StreamReader sr = new StreamReader(cdpError, Encoding.GetEncoding(1251)))
                    {
                        sr.BaseStream.Seek(0, SeekOrigin.Begin);
                        error = sr.ReadLine();
                        sr.Close();
                    }
                }

                if (File.Exists(outCdp))
                {
                    using (StreamReader sr = new StreamReader(outCdp, Encoding.GetEncoding(1251)))
                    {
                        sr.BaseStream.Seek(0, SeekOrigin.Begin);
                        outdata = sr.ReadLine();
                        sr.Close();
                    }
                }
            }
            return String.IsNullOrEmpty(error);
        }
    }
}
