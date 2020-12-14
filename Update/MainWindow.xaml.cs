using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
            String[] http_urls = new string[] { "https://repo.anaconda.com/archive/Anaconda3-2020.11-Windows-x86_64.exe",
                                                "https://download-cf.jetbrains.com/python/pycharm-community-2020.3.exe",
                                                   };   
            String[] save_urls = new string[] { @"C:\Users\12132\Desktop\1.zip",
                                                @"C:\Users\12132\Desktop\2.zip",
                                                   };
            List<String> exist_http_urls = new List<string>();
            List<String> exist_save_urls = new List<string>();
            for (int i=0; i<http_urls.Length; i++)
            {   
                if (HttpFileExist(http_urls[i]))
                {
                    exist_http_urls.Add(http_urls[i]);
                    exist_save_urls.Add(save_urls[i]);
                }
            }
            DownloadHttpFile(exist_http_urls, exist_save_urls);
        }

        //需要处理下载失败
        public void DownloadHttpFile(List<String> http_urls, List<String> save_urls)
        {
            WebResponse response = null;
            //获取远程文件
            List<WebRequest> requests = new List<WebRequest>();
            List<String> fileNames = new List<String>();
            int i = 0;
            foreach (var urls in http_urls)
            {
                requests.Add(WebRequest.Create(urls));
                response = requests[i].GetResponse();
                if (response == null) return;
                //读远程文件的大小
                pbDown.Maximum += response.ContentLength;
                fileNames.Add(response.ResponseUri.Segments[response.ResponseUri.Segments.Length - 1]);
                i++;
            }

            
            //下载远程文件
            ThreadPool.QueueUserWorkItem((obj) =>
            {
                long readCount = 0;
                for (int k=0; k< requests.Count; k++)
                {
                    Stream netStream = requests[k].GetResponse().GetResponseStream();
                    Stream fileStream = new FileStream(save_urls[k], FileMode.Create);
                    byte[] read = new byte[1024];
                    int realReadLen = netStream.Read(read, 0, read.Length);
                    long oldTick = DateTime.Now.Ticks / 10000;
                    long interval = 0;
                    int downloadSpeed = 0;
                    while (realReadLen > 0)
                    {
                        readCount += (long)realReadLen;
                        interval = (DateTime.Now.Ticks / 10000) - oldTick;
                        if (interval > 800)
                        {
                            downloadSpeed = (int)(readCount * 1000L / 1024L / interval);
                            readCount = 0;
                            oldTick = DateTime.Now.Ticks / 10000;
                        }
                        fileStream.Write(read, 0, realReadLen);
                        //progressBarValue += realReadLen;
                        object[] param = new object[] { realReadLen, fileNames[k], downloadSpeed};
                        pbDown.Dispatcher.BeginInvoke(new ProgressBarSetter(SetProgressBar), param);
                        realReadLen = netStream.Read(read, 0, read.Length);

                       
                    }
                    fileStream.Close();
                    netStream.Close();
                }
                
            }, null);
           
        }

        private bool HttpFileExist(string http_file_url)
        {
            WebResponse response = null;
            bool result = false;//下载结果
            try
            {
                response = WebRequest.Create(http_file_url).GetResponse();
                result = response == null ? false : true;
            }
            catch (Exception ex)
            {
                result = false;
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                }
            }
            return result;
        }

        public delegate void ProgressBarSetter(double value, String fileName, int downloadSpeed);
        public void SetProgressBar(double value, String fileName, int downloadSpeed)
        {
            //显示进度条
            pbDown.Value += value;
            //显示百分比
            tb_percent.Text = ((pbDown.Value / pbDown.Maximum) * 100).ToString("f1") + "%  ";
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
            tb.Text = fileName;
        }
    }
}
