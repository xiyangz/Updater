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
    public class BaseFileInfo
    {
        public string file_name;
        public string save_dir;

        public BaseFileInfo(string _file_name, string _save_dir)
        {
            file_name = _file_name;
            save_dir = _save_dir;
        }

        public string GetRelativePath()
        {
            return save_dir + file_name;
        }
    }

    public class FileInfo : BaseFileInfo
    {
        public long file_sizes;
        public string download_url;

        public FileInfo(string _file_name, long _file_sizes, string _download_url, string _save_dir)
            :base(_file_name,_save_dir)
        {
            file_sizes = _file_sizes;
            download_url = _download_url;
        }
    }

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
                //tb_test.FontSize = 10;
                for (int i = 1; i < pargs.Length; i += 2)
                {
                    //tb_test.Text += pargs[i] + pargs[i + 1];
                    cmdParam.Add(pargs[i], pargs[i + 1]);
                }
                int current_version = int.Parse(cmdParam["-v"]);//版本，如：1001215 后四位为月日前面为版本
                string sel_language = cmdParam["-l"]; //语言，如：zh-CN
                string update_url = cmdParam["-u"];  //地址，如: https://update.aerosim.com.cn/aeroconnector_update/
                //找到需要迭代的版本  比如从 1001215 升级到 1031215 需要迭代1011215 1021215 1031215三个版本
                JObject jobj = JObject.Parse(GetUpdateInfoFromUrl(update_url + "versions.json"));//json字符串{"current_ver": 1021215,"vesions": [{ "1021215": 1011215},{"1011215" : 1001215},{ "1001215" : 0}]}
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
                //1.删除需更新的旧文件，然后下载更新新文件。2.增加每个版本新增文件。3.删除迭代版本的下个版本中删除的文件。
                //版本在list中越靠前越新  所以从后往前遍历
                string version_str = "";
                int version = 0;
                string update_info = "";

                List<BaseFileInfo> insert_files = new List<BaseFileInfo>();
                Dictionary<string, int> updateFilsMap = new Dictionary<string, int>();//保存update文件下标 与下面变量配合 检查是否有重复更新文件（在多个版本迭代中）
                List<BaseFileInfo> update_files = new List<BaseFileInfo>();
                List<BaseFileInfo> delete_files = new List<BaseFileInfo>();
                for (int i = span_versions.Count-1; i >= 0; i--) 
                {
                    jobj = JObject.Parse(GetUpdateInfoFromUrl(update_url + "updateinfo_" + span_versions[i] + ".json"));
                    if(i == span_versions.Count - 1)//当前版本的下个版本 获取它的delete_file
                    {
                        foreach (var file in jobj["delete_files"])
                        {
                            delete_files.Add(new BaseFileInfo((string)file["file_name"],
                                                              (string)file["save_dir"]));
                        }
                    }
                    else if(i == 0)//最新版 获取它的更新描述
                    {
                        version_str = (string)jobj["version_str"];
                        version = (int)jobj["version"];
                        update_info = (string)jobj["update_info"];
                    }
                    foreach (var file in jobj["insert_files"])
                    {
                        insert_files.Add(new FileInfo((string)file["file_name"],
                                                      (long)file["file_size"],
                                                      (string)file["download_url"],
                                                      (string)file["save_dir"] + (string)file["file_name"]));
                    }
                    //从旧到新版本  新增文件中将新版本相同名文件覆盖旧版本  不同名则保留
                    foreach (var file in jobj["update_files"])
                    {
                        string file_name = (string)file["file_name"];
                        int index = -1;
                        if (updateFilsMap.TryGetValue(file_name, out index))//成功则说明已经有文件
                        {//不新增而是覆盖
                            update_files[index] = new FileInfo((string)file["file_name"],
                                                               (long)file["file_size"],
                                                               (string)file["download_url"],
                                                               (string)file["save_dir"] + (string)file["file_name"]);
                        }
                        else//失败则说明是新增文件
                        {
                            updateFilsMap.Add(file_name, update_files.Count);
                            update_files.Add(new FileInfo((string)file["file_name"],
                                                          (long)file["file_size"],
                                                          (string)file["download_url"],
                                                          (string)file["save_dir"] + (string)file["file_name"]));
                        }
                    }

                }
                
                tb_info.Text = update_info;
                tb_version.Text += version_str;

                ThreadPool.QueueUserWorkItem((obj) =>
                {
                    isOthersStop = false;
                    //删除旧文件
                    if (!DeleteDeprecatedFile(delete_files, update_files))
                    {
                        CancelUpdate();
                        Dispatcher.BeginInvoke(new CloseWindowSetter(CloseWindow));
                        return;
                    }
                    
                    //下载更新文件
                    DownloadHttpFile(insert_files, update_files);

                    isOthersStop = true;
                    btn_cancel.Dispatcher.BeginInvoke(new CancelButtonDisableSetter(SetCancelButtonDisable));
                    //删除临时备份文件
                }, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                try
                {
                    CancelUpdate();
                    Close();  
                }
                catch
                {

                }

                
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
        public void DownloadHttpFile(List<BaseFileInfo> insert_files, List<BaseFileInfo> update_files)
        {
            List<BaseFileInfo>[] flLists = new List<BaseFileInfo>[2] { insert_files, update_files };

            long max = 0;
            foreach (var files in flLists)
            {
                foreach (var file in files)
                {
                    max += ((FileInfo)file).file_sizes;
                }
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
            foreach (var files in flLists)
            {
                requests.Clear();
                for (int k = 0; k < files.Count; k++)
                {
                    FileInfo fi = (FileInfo)files[k];
                    requests.Add(WebRequest.Create(fi.download_url));
                    requests[k].Timeout = 1000;
                    Console.WriteLine("0");
                    while (IsFileInUse(fi.save_dir) && !isCancel)
                    {
                        sp2.Dispatcher.BeginInvoke(new FileUsedSetter(DisplayFileUsed), fi.file_name);
                        Thread.Sleep(500);
                    }

                    string tempPath = System.IO.Path.GetTempPath();//临时文件夹
                    string rdFileName = System.IO.Path.GetRandomFileName();//临时文件名 
                    int tryCount = 0;
                    while (IsFileInUse(tempPath + rdFileName) && tryCount++ < 10)
                        rdFileName = System.IO.Path.GetRandomFileName();
                    if (tryCount >= 10)
                        throw new Exception("Try random file name fail! ");
                    if (File.Exists(fi.save_dir))
                    {
                        File.Copy(fi.save_dir, tempPath + rdFileName);//先把原先文件复制到临时文件中
                        tempFileMap.Add(fi.save_dir, tempPath + rdFileName);//记录此次复制双方文件地址
                    }
                    string fullPath = System.IO.Path.GetDirectoryName(fi.save_dir);
                    if (fullPath != "" && !Directory.Exists(fullPath))
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                    Stream fileStream = new FileStream(fi.save_dir, FileMode.Create);
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
                        object[] param = new object[] { realReadLen, fi.file_name, downloadSpeed };
                        sp2.Dispatcher.BeginInvoke(new ProgressBarSetter(SetProgressBar), param);
                        realReadLen = netStream.Read(read, 0, read.Length);
                        Thread.Sleep(1);

                    }
                    fileStream.Close();
                    netStream.Close();
                }
                
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
        public bool DeleteDeprecatedFile(List<BaseFileInfo> _delete_files,List<BaseFileInfo> _update_files)
        {
            try
            {
                List<BaseFileInfo>[] flLists = new List<BaseFileInfo>[2] { _delete_files, _update_files };

                foreach (var files in flLists)
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        string tempPath = System.IO.Path.GetTempPath();//临时文件夹
                        string rdFileName = System.IO.Path.GetRandomFileName();//临时文件名 
                        int tryCount = 0;
                        while (IsFileInUse(tempPath + rdFileName) && tryCount++ < 10)
                            rdFileName = System.IO.Path.GetRandomFileName();
                        if (tryCount >= 10)
                            throw new Exception("Try random file name fail! ");

                        try
                        {
                            File.Copy(files[i].GetRelativePath(), tempPath + rdFileName);//先把原先文件复制到临时文件中
                            tempFileMap.Add(files[i].GetRelativePath(), tempPath + rdFileName);//记录此次复制双方文件地址
                        }
                        catch(FileNotFoundException)
                        {
                            continue;
                        }
                        DelFile:
                        try { File.Delete(files[i].GetRelativePath());}
                        catch (IOException)
                        {//一般只会是文件占用导致这个异常
                            tb.Dispatcher.Invoke(new FileUsedSetter(DisplayFileUsed), files[i].file_name);
                            Thread.Sleep(1000);
                            if (!isCancel)
                                goto DelFile;
                            else
                                return false;
                        }
                        catch (Exception e){ Console.WriteLine(e.Message);}
                    }
                }
            }
            catch (Exception e) 
            {
                Console.WriteLine(e.Message);
                return false;
            }
            return true;
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
