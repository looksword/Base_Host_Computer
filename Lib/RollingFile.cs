using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Lib
{
    public class RollingFileWriterManager
    {
        private string outputPath = ".\\";
        private string fileNameSuffix = "log";
        private int maxFileKiloByte = 10;
        private int maxFileNum = 10;
        private Dictionary<string, RollingFileWriter> rfwDict = new Dictionary<string, RollingFileWriter>();
        private Thread driver = null;
        private int writeInterval = 2000;

        public RollingFileWriterManager(int maxFileKiloByte, int maxFileNum)
        {
            this.maxFileKiloByte = maxFileKiloByte;
            this.maxFileNum = maxFileNum;
        }

        /// <summary>
        /// 输出路径
        /// </summary>
        public string OutputPath
        {
            get { return outputPath; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    outputPath = ".\\";
                    return;
                }
                if (value.EndsWith("\\") || value.EndsWith("/"))
                    outputPath = value;
                else
                    outputPath = value + "\\";
            }
        }

        /// <summary>
        /// 文件名后缀
        /// </summary>
        public string FileNameSuffix
        {
            get { return fileNameSuffix; }
            set
            {
                fileNameSuffix = value.Replace("\\", "");
                fileNameSuffix = fileNameSuffix.Replace("/", "");
                fileNameSuffix = fileNameSuffix.Replace(".", "");
            }
        }

        /// <summary>
        /// 写间隔
        /// </summary>
        public int WriteInterval
        {
            get { return writeInterval; }
            set
            {
                writeInterval = value;
                if (writeInterval < 18)
                    writeInterval = 18;
                if (writeInterval > 18000)
                    writeInterval = 18000;
            }
        }

        /// <summary>
        /// 添加数据，带路径
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="machineID"></param>
        /// <param name="topic"></param>
        /// <param name="data"></param>
        public void AddDataWithPath(string filePath, string machineID, string topic, string data)
        {
            lock (this)
            {
                string key = machineID + topic;
                if (!rfwDict.ContainsKey(key))
                {
                    rfwDict[key] = new RollingFileWriter(topic, filePath, machineID, fileNameSuffix, maxFileKiloByte, maxFileNum);
                }
                rfwDict[key].AddData(data);
            }
        }

        /// <summary>
        /// 添加数据
        /// </summary>
        /// <param name="machineID"></param>
        /// <param name="topic"></param>
        /// <param name="data"></param>
        public void AddData(string machineID, string topic, string data)
        {
            AddDataWithPath(outputPath, machineID, topic, data);
        }

        /// <summary>
        /// 开始写
        /// </summary>
        public void StartWrite()
        {
            if (driver != null)
                return;

            stop = false;
            driver = new Thread(new ThreadStart(DriveFileWriter));
            driver.Start();
        }

        private bool stop = false;
        /// <summary>
        /// 停止写
        /// </summary>
        public void StopWrite()
        {
            if (driver == null)
                return;

            stop = true;
            driver.Join();
            driver = null;
        }

        public delegate void ErrMsgHandler(string errMsg);
        public event ErrMsgHandler OnErrMsg = null;

        /// <summary>
        /// 写文件驱动
        /// </summary>
        private void DriveFileWriter()
        {
            while (true)
            {
                if (stop)
                    break;

                try
                {
                    lock (this)
                    {
                        foreach (RollingFileWriter rfw in rfwDict.Values)
                        {
                            if (stop)
                                break;
                            try
                            {
                                rfw.WriteFile();
                            }
                            catch (Exception ex)
                            {
                                if (OnErrMsg != null)
                                    OnErrMsg(ex.Message);
                            }
                        }
                    }
                }
                catch (Exception) { }

                int accuInterval = 0;
                while (true)
                {
                    if (stop)
                        break;
                    Thread.Sleep(18);
                    accuInterval += 18;
                    if (accuInterval >= writeInterval)
                        break;
                }
            }
        }
    }

    public class RollingFileWriter
    {
        private string topic = "";
        private string outputPath = ".\\";
        private string suffix = "";
        private int maxFileByte = 10240;
        private int maxFileNum = 10;
        private int currFileCount = 0;
        private List<string> dataList = new List<string>();

        public RollingFileWriter(string topic, string outputPath, string subPath, string suffix, int maxFileKiloByte, int maxFileNum)
        {
            this.topic = topic;
            this.outputPath = outputPath;
            if (!string.IsNullOrEmpty(subPath))
                this.outputPath = outputPath + subPath + "\\";
            this.suffix = suffix;
            this.maxFileByte = maxFileKiloByte * 1024;
            this.maxFileNum = maxFileNum;

            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(this.outputPath);
                if (!dirInfo.Exists)
                    dirInfo.Create();
                FileInfo[] files = dirInfo.GetFiles(topic + "_*." + suffix, SearchOption.TopDirectoryOnly);
                FileInfo currFileInfo = null;
                DateTime fileTime = DateTime.MinValue;
                if ((files != null) && (files.Length > 0))
                {
                    foreach (FileInfo file in files)//Find the last write file.
                    {
                        string serialStr = file.Name.Replace(topic + "_", "");
                        serialStr = serialStr.Replace("." + suffix, "");
                        try
                        {
                            int serial = int.Parse(serialStr);
                            if (fileTime < file.LastWriteTime)
                            {
                                fileTime = file.LastWriteTime;
                                currFileCount = serial;
                                currFileInfo = file;
                            }
                        }
                        catch (Exception) { }
                    }
                }

                if (currFileInfo.Length > maxFileByte)
                {
                    currFileCount++;//Turn to next file
                    if ((maxFileNum > 0) && (currFileCount >= maxFileNum))
                        currFileCount = 0;
                    string fileName = topic + "_" + currFileCount.ToString() + "." + suffix;
                    files = dirInfo.GetFiles(fileName, SearchOption.TopDirectoryOnly);
                    if ((files != null) && (files.Length > 0))
                    {
                        files[0].Delete();
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 添加数据
        /// </summary>
        /// <param name="data"></param>
        public void AddData(string data)
        {
            lock (this)
            {
                dataList.Add(data);
            }
        }

        /// <summary>
        /// 写入文件
        /// </summary>
        internal void WriteFile()
        {
            lock (this)
            {
                if (dataList.Count <= 0)
                    return;
            }

            FileStream fs = null;
            long fileLen = 0;
            string fileName = outputPath + topic + "_" + currFileCount.ToString() + "." + suffix;
            try
            {
                fs = new FileStream(fileName, FileMode.Append);
                if (fs.Position == 0)
                {
                    fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
                }
            }
            catch
            {
                fs = new FileStream(fileName, FileMode.Create);
                fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
            }

            try
            {
                //Encoding e = Encoding.GetEncoding("gb2312");
                //if (e == null)
                //{
                //    e = Encoding.Default;
                //}
                while (true)
                {
                    lock (this)
                    {
                        if (dataList.Count > 0)
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(dataList[0]);
                            //byte[] bytes = e.GetBytes(dataList[0]);
                            fs.Write(bytes, 0, bytes.Length);
                            dataList.RemoveAt(0);
                            //if (fs.Length > maxFileByte)
                            //    break;
                        }
                        else break;
                    }
                }
            }
            finally
            {
                fileLen = fs.Length;
                fs.Close();
            }

            if (fileLen > maxFileByte)
            {
                currFileCount++;//Turn to next file
                if ((maxFileNum > 0) && (currFileCount >= maxFileNum))
                    currFileCount = 0;
                fileName = topic + "_" + currFileCount.ToString() + "." + suffix;

                DirectoryInfo dirInfo = new DirectoryInfo(outputPath);
                FileInfo[] files = dirInfo.GetFiles(fileName, SearchOption.TopDirectoryOnly);
                if ((files != null) && (files.Length > 0))
                {
                    files[0].Delete();
                }
            }
        }
    }
}
