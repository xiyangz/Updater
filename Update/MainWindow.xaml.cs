using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Shapes;
using Newtonsoft.Json.Linq;

namespace Update
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {//进行json请求
            try
            {
                string[] pargs = Environment.GetCommandLineArgs();
                //保存 -v当前版本 -u更新地址 -l显示语言 -j版本json文件   更新地址+版本文件能得到文件地址
                Dictionary<string, string> cmdParam = new Dictionary<string, string>();
                string exePath = System.IO.Path.GetDirectoryName(pargs[0]);
                tb_test.FontSize = 10;
                for (int i = 1; i < pargs.Length; i += 2)
                {
                    tb_test.Text += pargs[i] + pargs[i + 1];
                    cmdParam.Add(pargs[i], pargs[i + 1]);
                }
                int current_version = int.Parse(cmdParam["-v"]);//版本，如：100
                string sel_language = cmdParam["-l"]; //语言，如：zh-CN
                string update_url = cmdParam["-u"];  //地址，如: https://update.aerosim.com.cn/aeroconnector_update/
                //找到需要迭代的版本  比如从 100 升级到 103 需要迭代101 102 103三个版本
                JObject jobj = JObject.Parse(GetUpdateInfoFromUrl(update_url + "versions.json"));//json字符串{"current_ver": 102,"vesions": [{ "102": 101},{"101" : 100},{ "100" : 0}]}
                int newest_version = (int)jobj["current_ver"];
                List<int> span_versions = new List<int>();
                int ver = (int)jobj["vesions"][Convert.ToString(newest_version)];
                while(ver != current_version)
                {
                    span_versions.Add(ver);
                    ver = (int)jobj["vesions"][Convert.ToString(ver)];
                }
                //1.剔除旧版本在新版本也更新的文件，2.增加旧版本新增新版本没更新的文件 和 3.增加新版本中旧版本没有的文件



                string version_str = (string)jobj["version_str"];
                int version = (int)jobj["version"];
                string update_info = (string)jobj["update_info"];
                List<string> file_names = new List<string>();
                List<long> file_sizes = new List<long>();
                List<string> dwld_urls = new List<string>();
                List<string> save_urls = new List<string>();

                foreach (var file in jobj["files"])
                {
                    file_names.Add((string)file["file_name"]);
                    file_sizes.Add((long)file["file_size"]);
                    dwld_urls.Add((string)file["download_url"]);
                    save_urls.Add((string)file["save_url"] + (string)file["file_name"]);
                }

                tb_info.Text = update_info;
                tb_version.Text += version_str;

                ThreadPool.QueueUserWorkItem((obj) =>
                {
                    //List<String> exist_http_urls = new List<string>();
                    //List<String> exist_save_urls = new List<string>();
                    //for (int i = 0; i < http_urls.Length; i++)
                    //{
                    //    if (HttpFileExist(http_urls[i]))
                    //    {
                    //        exist_http_urls.Add(http_urls[i]);
                    //        exist_save_urls.Add(save_urls[i]);
                    //    }
                    //}
                    DownloadHttpFile(file_names, file_sizes, dwld_urls, save_urls);
                }
                , null);
            }
            catch (Exception)
            {
                MessageBox.Show("出现错误，更新失败");
            }
        }
        public string GetUpdateInfoFromUrl(string http_url)
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

        //需要处理下载失败
        public void DownloadHttpFile(List<string> file_names, List<long> file_sizes, List<string> http_urls, List<string> save_urls)
        {
            long max = 0;
            foreach (var size in file_sizes)
            {
                max += size;
            }
            pbDown.Dispatcher.BeginInvoke(new ProgressBarMaximumSetter(SetProgressBarMaximum), max);

            //下载远程文件
            //获取远程文件
            List<WebRequest> requests = new List<WebRequest>();
            byte[] read = new byte[1024 * 1024];
            long readCount = 0;
            int downloadSpeed = 0;
            int interval_min = 50;
            int interval_max = 800;
            long interval = interval_min;
            long tick = 0;
            for (int k=0; k< http_urls.Count; k++)
            {
                requests.Add(WebRequest.Create(http_urls[k]));
                while (IsFileInUse(save_urls[k]))
                {
                    sp2.Dispatcher.BeginInvoke(new FileUsedSetter(DisplayFileUsed), file_names[k]);
                    Thread.Sleep(500);
                }
                Stream fileStream = new FileStream(save_urls[k], FileMode.Create);
                Stream netStream = requests[k].GetResponse().GetResponseStream();
                long oldTick = DateTime.Now.Ticks / 10000;
                int realReadLen = netStream.Read(read, 0, read.Length);
                while (realReadLen > 0)
                {
                    readCount += (long)realReadLen;
                    tick = (DateTime.Now.Ticks / 10000) - oldTick;
                    if (tick > interval)
                    {
                        downloadSpeed = (int)(readCount * 1000L / 1024L / tick);
                        readCount = 0;
                        oldTick = DateTime.Now.Ticks / 10000;
                        if (interval < interval_max)
                            interval += 50;
                    }

                    fileStream.Write(read, 0, realReadLen);
                    //progressBarValue += realReadLen;
                    object[] param = new object[] { realReadLen, file_names[k], downloadSpeed };
                    sp2.Dispatcher.BeginInvoke(new ProgressBarSetter(SetProgressBar), param);
                    realReadLen = netStream.Read(read, 0, read.Length);
                    Thread.Sleep(1);
                       
                }
                fileStream.Close();
                netStream.Close();
            }
           
        }

        public delegate void ProgressBarMaximumSetter(long max);
        public void SetProgressBarMaximum(long max)
        {
            pbDown.Maximum = max;
        }

        public delegate void ProgressBarSetter(double value, String fileName, int downloadSpeed);
        public void SetProgressBar(double value, String fileName, int downloadSpeed)
        {
            //显示进度条
            pbDown.Value += value;
            //显示百分比
            tb_percent.Text = ((pbDown.Value / pbDown.Maximum) * 100).ToString("f1") + "% - ";
            if(downloadSpeed > 1024)
            {
                tb_percent.Text += ((double)downloadSpeed / 1024).ToString("f1") + " MB/s";
            }
            else if (downloadSpeed > 1024 * 1024)
            {
                tb_percent.Text += ((double)downloadSpeed / 1024 / 1024).ToString("f1") + " GB/s";
            }
            else
            {
                tb_percent.Text += downloadSpeed + " KB/s";
            }
            int remain_sec = (int)((pbDown.Maximum - pbDown.Value) / (downloadSpeed * 1024));
            tb_percent.Text += " - ";
            if (remain_sec / (60 * 60) > 0)
            {
                tb_percent.Text += (int)(remain_sec/(60*60)) + "h ";
                remain_sec %= 60 * 60;
            }
            if (remain_sec / 60 > 0)
            {
                tb_percent.Text += (int)(remain_sec / 60) + "m ";
                remain_sec %= 60;
            }
            tb_percent.Text += remain_sec + "s ";
            tb.Text = fileName;
        }

        public delegate void FileUsedSetter(string fileName);
        public void DisplayFileUsed(string fileName)
        {
            tb.Text = "\""+ fileName + "\"" + "正在被其他程序占用...";
            tb_percent.Text = ((pbDown.Value / pbDown.Maximum) * 100).ToString("f1") + "%";
        }
     

        //监测文件是否被其他软件使用
        public static bool IsFileInUse(string fileName)
        {
            bool inUse = true;

            FileStream fs = null;
            try
            {

                fs = new FileStream(fileName, FileMode.Open, FileAccess.Read,

                FileShare.None);

                inUse = false;
            }
            catch(Exception)
            {

            }
            finally
            {
                if (fs != null)

                    fs.Close();
            }
            return inUse;//true表示正在使用,false没有使用
        }
    }
}
