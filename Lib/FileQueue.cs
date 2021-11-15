using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Lib
{
    /// <summary>
    /// 文件堆栈
    /// </summary>
    public class FileQueue
    {
        private string QueueName = "file";
        private string FileQueuePre = "";
        private string FileQueueRunPath = "";
        private string FileQueueRunIndexPath = "";
        private string FileQueueCountPath = "";

        public FileQueue(string queueName = "file")
        {
            this.QueueName = queueName;
            string DirPath = Path.Combine(AsyncPerformer.AppDirectory, "queue");
            if (!Directory.Exists(DirPath))
            {
                Directory.CreateDirectory(DirPath);
            }
            this.FileQueuePre = Path.Combine(DirPath, queueName);
            this.FileQueueRunPath = Path.Combine(DirPath, queueName + "_run.dat");
            this.FileQueueRunIndexPath = Path.Combine(DirPath, queueName + "_run_index.dat");
            this.FileQueueCountPath = Path.Combine(DirPath, queueName + "_count.dat");
        }

        /// <summary>
        /// 插入堆栈
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public bool Push(string[] strList)
        {
            int tryIndex = 0;
            string filePath = this.FileQueuePre + "_list" + DateTime.Now.ToString("_yyyyMMdd") + ".dat";

            while (tryIndex < 5)
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(filePath, true))
                    {
                        foreach (var str in strList)
                        {
                            sw.WriteLine(str);
                        }
                    }

                    SetCount(strList.Length);

                    return true;
                }
                catch// (Exception ex)
                {
                    tryIndex++;
                    Thread.Sleep(100);
                }
            }

            return false;
        }

        // 设置队列待处理数量
        private int SetCount(int i)
        {
            int count = 0;
            if (File.Exists(this.FileQueueCountPath))
            {
                count = int.Parse(File.ReadAllText(this.FileQueueCountPath));
            }

            count += i;

            File.WriteAllText(this.FileQueueCountPath, count.ToString());

            return count;
        }

        /// <summary>
        /// 清空堆栈
        /// </summary>
        /// <returns></returns>
        public bool Clear()
        {
            string[] fileList = Directory.GetFiles(AsyncPerformer.AppDirectory + "\\queue\\", QueueName + "_*.dat");

            foreach (var file in fileList)
            {
                File.Delete(file);
            }

            return true;
        }

        /// <summary>
        /// 堆栈待处理数量
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            int count = 0;
            if (File.Exists(this.FileQueueCountPath))
            {
                count = int.Parse(File.ReadAllText(this.FileQueueCountPath));
            }

            return count;
        }

        /// <summary>
        /// 获取待处理列表
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public List<string> GetList(int count = 1)
        {
            List<string> list = new List<string>();

            bool isFirst = false;
            if (!File.Exists(this.FileQueueRunPath))
            {
                string[] fileList = Directory.GetFiles(AsyncPerformer.AppDirectory + "\\queue\\", QueueName + "_list_*.dat");
                if (fileList.Length == 0)
                {
                    return list;
                }

                Array.Sort(fileList);

                File.Move(fileList[0], this.FileQueueRunPath);
                isFirst = true;
            }

            int startIndex = 0;
            int totalCount = 0;

            if (File.Exists(this.FileQueueRunIndexPath))
            {
                string strIndex = File.ReadAllText(this.FileQueueRunIndexPath);
                string[] arrIndex = strIndex.Split(',');

                startIndex = int.Parse(arrIndex[0]);
                totalCount = int.Parse(arrIndex[1]);
            }

            int index = 0;
            using (StreamReader sm = File.OpenText(this.FileQueueRunPath))
            {
                while (true)
                {
                    string str = sm.ReadLine();
                    if (str == null)
                    {
                        break;
                    }
                    str = str.Trim();

                    if (str == "")
                    {
                        continue;
                    }

                    totalCount++;
                    if (index < startIndex)
                    {
                        index++;
                        continue;
                    }

                    if (list.Count < count)
                    {
                        list.Add(str);
                    }

                    if (list.Count >= count && !isFirst)
                    {
                        break;
                    }
                }
            }

            if (isFirst)
            {
                File.WriteAllText(this.FileQueueRunIndexPath, "0," + totalCount);
            }

            return list;
        }

        /// <summary>
        /// 出栈
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public bool Pop(int count = 1)
        {
            if (!File.Exists(this.FileQueueRunIndexPath))
            {
                return false;
            }

            string strIndex = File.ReadAllText(this.FileQueueRunIndexPath);
            string[] arrIndex = strIndex.Split(',');

            int startIndex = int.Parse(arrIndex[0]) + count;
            int totalCount = int.Parse(arrIndex[1]);

            SetCount(-1 * count);

            if (startIndex >= totalCount)
            {
                File.Delete(this.FileQueueRunIndexPath);
                File.Delete(this.FileQueueRunPath);
            }
            else
            {
                File.WriteAllText(this.FileQueueRunIndexPath, startIndex + "," + totalCount);
            }

            return true;
        }
    }
}
