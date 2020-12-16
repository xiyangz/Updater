using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace Update
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private Dictionary<string, string> tempFileMap = new Dictionary<string, string>();
        private bool isCancel = false;//是否取消
        private bool isOthersStop = true; //是否其他进程停止

        public MainWindow()
        {
            InitializeComponent();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {//进行json请求
            try
            {
                string[] pargs = Environment.GetCommandLineArgs();
                if (pargs.Length < 2)
                {
                    Close();
                    return;
                }
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
                int ver = newest_version;
                while (ver != current_version)
                {
                    span_versions.Add(ver);
                    ver = (int)jobj["versions"][Convert.ToString(ver)];
                }
                if (span_versions.Count < 1)//不需要更新
                    throw new Exception("no need update!");
                //1.剔除旧版本在新版本也更新的文件，2.增加每个版本新增新版本没更新的文件 和 3 删除当前版本的下个版本中删除的文件
                //版本在list中越靠前越新  所以从后往前遍历
                string version_str = "";
                int version = 0;
                string update_info = "";

                Dictionary<string, int> updateFilsMap = new Dictionary<string, int>();//保存update文件下标 与下面变量配合
                List<string> update_file_names = new List<string>();
                List<long> update_file_sizes = new List<long>();
                List<string> update_dwld_urls = new List<string>();
                List<string> update_save_urls = new List<string>();

                List<string> delete_file_names = new List<string>();
                List<string> delete_save_urls = new List<string>();
                for (int i = span_versions.Count-1; i >= 0; i--) 
                {
                    jobj = JObject.Parse(GetUpdateInfoFromUrl(update_url + "updateinfo_" + span_versions[i] + ".json"));
                    if(i == span_versions.Count - 1)//当前版本的下个版本 获取它的delete_file
                    {
                        foreach (var file in jobj["delete_files"])
                        {
                            delete_file_names.Add((string)file["file_name"]);
                            delete_save_urls.Add((string)file["save_url"]);
                        }
                    }
                    else if(i == 0)//最新版 获取它的更新描述
                    {
                        version_str = (string)jobj["version_str"];
                        version = (int)jobj["version"];
                        update_info = (string)jobj["update_info"];
                    }
                    //从旧到新版本  将新版本相同名文件覆盖旧版本  不同名则保留
                    foreach (var file in jobj["update_files"])
                    {
                        string file_name = (string)file["file_name"];
                        int index = -1;
                        if (updateFilsMap.TryGetValue(file_name, out index))//成功则说明已经有文件
                        {//不新增而是覆盖
                            update_file_names[index] = file_name;
                            update_file_sizes[index] = (long)file["file_size"];
                            update_dwld_urls[index] = (string)file["download_url"];
                            update_save_urls[index] = (string)file["save_url"] + (string)file["file_name"];
                        }
                        else//失败则说明是新增文件
                        {
                            updateFilsMap.Add(file_name, update_file_names.Count);

                            update_file_names.Add(file_name);
                            update_file_sizes.Add((long)file["file_size"]);
                            update_dwld_urls.Add((string)file["download_url"]);
                            update_save_urls.Add((string)file["save_url"] + (string)file["file_name"]);
                        }
                    }

                }
                
                tb_info.Text = update_info;
                tb_version.Text += version_str;

                ThreadPool.QueueUserWorkItem((obj) =>
                {
                    isOthersStop = false;
                    //删除旧文件
                    DeleteDeprecatedFile(delete_file_names, delete_save_urls);
                    //下载更新文件
                    DownloadHttpFile(update_file_names, update_file_sizes, update_dwld_urls, update_save_urls);

                    isOthersStop = true;
                    btn_cancel.Dispatcher.BeginInvoke(new CancelButtonDisableSetter(SetCancelButtonDisable));
                    //删除临时备份文件
                }, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
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

       
       
        //需要处理下载失败 和 取消更新还原文件
        /*可能的失败 
        1.c盘空间不足 到时copy到temp文件夹失败
        2.文件一直被占用 只能取消更新
        3.网络错误
        4.

        */
        
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
                requests[k].Timeout = 1000;
                Console.WriteLine("0");
                while (IsFileInUse(save_urls[k]))
                {
                    sp2.Dispatcher.BeginInvoke(new FileUsedSetter(DisplayFileUsed), file_names[k]);
                    Thread.Sleep(500);
                }
       
                string tempPath = System.IO.Path.GetTempPath();//临时文件夹
                string rdFileName = System.IO.Path.GetRandomFileName();//临时文件名 
                int tryCount = 0;
                while (IsFileInUse(tempPath + rdFileName) && tryCount++ < 10)
                    rdFileName = System.IO.Path.GetRandomFileName();
                if (tryCount >= 10)
                    throw new Exception("Try random file name fail! ");
                if(File.Exists(save_urls[k]))
                {
                    File.Copy(save_urls[k], tempPath + rdFileName);//先把原先文件复制到临时文件中
                    tempFileMap.Add(save_urls[k], tempPath + rdFileName);//记录此次复制双方文件地址
                }
                string fullPath = System.IO.Path.GetDirectoryName(save_urls[k]);
                if (fullPath !=  "" && !Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }
                Stream fileStream = new FileStream(save_urls[k], FileMode.Create);
                Stream netStream = requests[k].GetResponse().GetResponseStream();
                long oldTick = DateTime.Now.Ticks / 10000;
                int realReadLen = netStream.Read(read, 0, read.Length);
                while (realReadLen > 0 && !isCancel)
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

        public delegate void CloseWindowSetter();
        public void CloseWindow()
        {
            Close();
        } 
        
        public delegate void CancelButtonDisableSetter();
        public void SetCancelButtonDisable()
        {
            btn_cancel.IsEnabled = false;
            btn_cancel.Opacity = 0.5;
        }

        public void CancelUpdate()
        {
            foreach (KeyValuePair<string, string> kv in tempFileMap)
            {
                File.Copy(kv.Value, kv.Key, true);
            }
            DelteTempFile();
        }
        
        //删除版本更新中弃用文件
        public void DeleteDeprecatedFile(List<string> file_names, List<string> save_urls)
        {
            try
            {
                for (int i = 0; i < file_names.Count; i++)
                {

                    string tempPath = System.IO.Path.GetTempPath();//临时文件夹
                    string rdFileName = System.IO.Path.GetRandomFileName();//临时文件名 
                    int tryCount = 0;
                    while (IsFileInUse(tempPath + rdFileName) && tryCount++ < 10)
                        rdFileName = System.IO.Path.GetRandomFileName();
                    if (tryCount >= 10)
                        throw new Exception("Try random file name fail! ");
                  
                    File.Copy(save_urls + file_names[i], tempPath + rdFileName);//先把原先文件复制到临时文件中
                    tempFileMap.Add(save_urls + file_names[i], tempPath + rdFileName);//记录此次复制双方文件地址
                  

                    File.Delete(save_urls[i] + file_names[i]);
                }
            }
            catch(Exception e) 
            {
                Console.WriteLine(e.Message);
            }
        }
        public void DelteTempFile()
        {
            foreach (var tempFile in tempFileMap.Values)
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {

                    }
                    
                }
            }
        }
        //监测文件是否被其他软件使用
        public bool IsFileInUse(string fileName)
        {
            Boolean result = false;

            //判断文件是否存在，如果不存在，直接返回 false
            if (!System.IO.File.Exists(fileName))
            {
                result = false;

            }//end: 如果文件不存在的处理逻辑
            else
            {//如果文件存在，则继续判断文件是否已被其它程序使用

                //逻辑：尝试执行打开文件的操作，如果文件已经被其它程序使用，则打开失败，抛出异常，根据此类异常可以判断文件是否已被其它程序使用。
                System.IO.FileStream fileStream = null;
                try
                {
                    fileStream = System.IO.File.Open(fileName, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);

                    result = false;
                }
                catch (System.IO.IOException ioEx)
                {
                    Console.WriteLine(ioEx.Message);
                    result = true;
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    result = true;
                }
                finally
                {
                    if (fileStream != null)
                    {
                        fileStream.Close();
                    }
                }

            }//end: 如果文件存在的处理逻辑

            //返回指示文件是否已被其它程序使用的值
            return result;
        }

       
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            switch (((Button)sender).Name)
            {
                case "btn_cancel":
                    {
                        isCancel = true;
                        btn_cancel.IsEnabled = false;
                        btn_cancel.Content = "正在取消";
                        btn_cancel.Opacity = 0.5;
                        ThreadPool.QueueUserWorkItem((obj) =>
                        {
                            while (!isOthersStop)
                                Thread.Sleep(100);
                            CancelUpdate();
                            Dispatcher.BeginInvoke(new CloseWindowSetter(CloseWindow));
                        }, null);
                    }
                    break;
                default:
                    break;
            }
        }
    }
}
