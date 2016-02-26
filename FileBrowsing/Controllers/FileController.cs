using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace FileBrowsing.Controllers
{
    // КЛАС ЛІЧИЛЬНИКА ФАЙЛІВ
    // накопичує та інкапсулює інформацію про кількість файлів і субдиректорій
    public class FileCounter
    {
        // Статичні дані лічильника: границі розмірів файлів, кількість байтів в мегабайті
        static protected uint[] limits = { 10, 50, 100 };
        static protected long MB = 1024 * 1024;

        // Дані поточного стану лічильника: файли різних розмірів, директорії, пропущені директорії
        protected uint[] files = { 0, 0, 0, 0 };
        protected uint dirs = 0;
        protected uint skippedDirs = 0;

        // Метод додавання нового файла за розміром
        public void AddFile(long size)
        {
            if (size <= limits[0] * MB) files[0]++;
            else if (size <= limits[1] * MB) files[1]++;
            else if (size <= limits[2] * MB) files[2]++;
            else files[3]++;
        }

        // Метод додавання даних з іншого лічильника
        public void AddCounter(FileCounter counter)
        {
            if (counter != null) {
                uint[] otherFiles = counter.GetFileCounts();
                for (int i = 0; i < files.Length; i++)
                    files[i] += otherFiles[i];
                dirs++;
                dirs += counter.GetDirs();
                skippedDirs += counter.GetSkippedDirs();
            }
        }

        // Метод пропуску директорії (у випадку відсутності доступу)
        public void SkipDirectory()
        {
            skippedDirs++;
        }

        // Методи отримання внутрішньої інформації - для менеджера директорій

        public uint[] GetLimits()
        {
            return limits;
        }

        public uint[] GetFileCounts()
        {
            return files;
        }

        public uint GetDirs()
        {
            return dirs;
        }

        public uint GetSkippedDirs()
        {
            return skippedDirs;
        }
    }

    // КЛАС МЕНЕДЖЕРА ДИРЕКТОРІЙ
    // обчислює та інкапсулює всю потрібну користувачеві інформацію про директорію
    public class DirectoryManager
    {
        // Статичний індикатор підрахунку (true - рахувати, false - зупинити)
        static protected bool counting = false;

        // Статичний метод рекурсивного підрахунку файлів і субдиректорій
        static public FileCounter Count(DirectoryInfo dir)
        {
            FileCounter counter = new FileCounter();
            try
            {
                foreach (DirectoryInfo subdir in dir.GetDirectories())
                    if (counting) counter.AddCounter(Count(subdir));
                foreach (FileInfo file in dir.GetFiles())
                    if (counting) counter.AddFile(file.Length);
                if (counting) return counter;
                else return null;
            }
            catch (Exception e)
            {
                counter.SkipDirectory();
                if (counting) return counter;
                else return null;
            }
        }

        // Дані поточної директорії: базова інформація і лічильник файлів
        protected DirectoryInfo currDir;
        protected FileCounter currCounter;

        // Конструктор менеджера директорій
        public DirectoryManager()
        {
            currDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        }

        // Метод запуску процесу підрахунку
        public void Count()
        {
            currCounter = null;
            counting = true;
            currCounter = Count(currDir);
        }

        // Метод зміни директорії за заданим номером
        public void ChangeDirectory(int num)
        {
            currCounter = null;
            counting = false;

            DirectoryInfo newDir = null;
            if ((currDir == null) && DriveInfo.GetDrives()[num].IsReady)
                newDir = DriveInfo.GetDrives()[num].RootDirectory;
            else {
                if (num == -2) newDir = currDir.Parent;
                else newDir = currDir.GetDirectories()[num];
            }
            currDir = newDir;
        }

        // Властивості з інформацією про директорію - для виводу на сторінці

        public string DirectoryName
        {
            get {
                if (currDir == null) return "Root";
                return currDir.FullName;
            }
        }

        public bool DiskRoot
        {
            get { 
                return (currDir == null); 
            }
        }

        public List<string> Subdirectories
        {
            get {
                List<string> dirList = new List<string>();
                if (currDir != null)
                    foreach (DirectoryInfo subdir in currDir.GetDirectories())
                        dirList.Add(subdir.Name);
                else
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                        dirList.Add(drive.Name);
                return dirList;
            }
        }

        public List<string> Files
        {
            get {
                List<string> fileList = new List<string>();
                if (currDir != null)
                    foreach (FileInfo file in currDir.GetFiles())
                        fileList.Add(file.Name);
                return fileList;
            }
        }

        public List<string> SizeLimits
        {
            get {
                if (currCounter == null) return null;
                List<string> limitList = new List<string>();
                uint[] limits = currCounter.GetLimits();
                limitList.Add(String.Format("Less {0}MB", limits[0]));
                limitList.Add(String.Format("{0}MB-{1}MB", limits[0], limits[1]));
                limitList.Add(String.Format("{0}MB-{1}MB", limits[1], limits[2]));
                limitList.Add(String.Format("More {0}MB", limits[2]));
                return limitList;
            }
        }

        public string TinyFileCount
        {
            get {
                if (currCounter == null) return null;
                return String.Format("{0}", currCounter.GetFileCounts()[0]);
            }
        }

        public string SmallFileCount
        {
            get {
                if (currCounter == null) return null;
                return String.Format("{0}", currCounter.GetFileCounts()[1]);
            }
        }

        public string BigFileCount
        {
            get {
                if (currCounter == null) return null;
                return String.Format("{0}", currCounter.GetFileCounts()[2]);
            }
        }

        public string HugeFileCount
        {
            get {
                if (currCounter == null) return null;
                return String.Format("{0}", currCounter.GetFileCounts()[3]);
            }
        }

        public string DirCount
        {
            get {
                if (currCounter == null) return null;
                return String.Format("Directories: {0}, skipped: {1}", 
                    currCounter.GetDirs(), currCounter.GetSkippedDirs());
            }
        }
    }

    // КЛАС WEB API КОНТРОЛЕРА
    // обробляє запити з веб-сторінки
    public class FileController : ApiController
    {
        // Статичний менеджер директорій
        static protected DirectoryManager mgr;

        // Метод отримання повної інформації про поточну папку: GET-метод без параметру, приклад URL - api/file
        public DirectoryManager GetAllInfo()
        {
            if (!mgr.DiskRoot) mgr.Count();
            return mgr;
        }

        // Метод зміни папки і отримання базової інформації: GET-метод з параметром, приклад URL - api/file/5
        public DirectoryManager GetDirectory(int id)
        {
            if (id == -1) mgr = new DirectoryManager();
            else mgr.ChangeDirectory(id);
            return mgr;
        }

        // Невикористовувані методи взаємодії зі сторінкою

        public void Post([FromBody]string value)
        {
        }

        public void Put(int id, [FromBody]string value)
        {
        }

        public void Delete(int id)
        {
        }
    }
}
