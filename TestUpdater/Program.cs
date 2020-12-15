using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace TestUpdater
{
    class Program
    {
        public static string update_url = "https://update.aerosim.com.cn/aeroconnector_update/";
        public static int current_version = 100;

        static void Main(string[] args)
        {
            //try
            //{
                string update_info = GetUpdateInfoFromUrl(update_url + "versions.json");
                JObject jobj = JObject.Parse(update_info);
                int version = (int)jobj["current_ver"];
                if (current_version < version)
                {//有新版本
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = "../Updater.exe"; //启动的应用程序名称
                    startInfo.Arguments = "-v " + current_version + " -l zh-CN -u " + update_url + " -j " + update_info;
                    startInfo.WindowStyle = ProcessWindowStyle.Normal;
                    Process.Start(startInfo);
                }
            //}
           // catch (Exception)
            //{
            //    Console.WriteLine("Error Occur");
            //}

            Console.WriteLine("Hello World!");
        }

        static public string GetUpdateInfoFromUrl(string http_url)
        {
            WebRequest request = WebRequest.Create(http_url);
            string urlContent = "";
            byte[] read = new byte[1024];
            Stream netStream = request.GetResponse().GetResponseStream();
            int realReadLen = netStream.Read(read, 0, read.Length);
            while (realReadLen > 0)
            {
                urlContent += System.Text.Encoding.Default.GetString(read);
                realReadLen = netStream.Read(read, 0, read.Length);
            }
            return urlContent;
        }
    }
}
