using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zip = Ionic.Zip;
using Backupme.Properties;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Telegram.Bot;

namespace Backupme
{
    public partial class Form1 : Form
    {

        static string[] Scopes = {  DriveService.Scope.Drive,
                           DriveService.Scope.DriveAppdata,                          
                           DriveService.Scope.DriveFile,
                           DriveService.Scope.DriveMetadataReadonly,
                           DriveService.Scope.DriveReadonly,
                           DriveService.Scope.DriveScripts };
        static string ApplicationName = "BackupME";
        UserCredential credential;

        const String Version = "0.1";        

        BackgroundWorker worker;
        BackgroundWorker uploader;

        String DataBasePath;
        String BackupPath;
        String ArchivePassword;
        String GDFolder;
        int BackupsCount;
        DateTime BackupTime;

        String GoogleClientID;
        String GoogleSecret;
        String TelegramChannelID;
        String TelegramToken;
        Boolean UseTelegram = false;

        TelegramBotClient BotClient;// =

        private System.Threading.Timer timer;

        long TotalBytesToUpload = 0;

        public Form1()
        {
            InitializeComponent();

            worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler(Worker_doWork);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Worker_Completed);

            uploader = new BackgroundWorker();
            uploader.DoWork += new DoWorkEventHandler(Uploader_doWork);
            uploader.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Uploader_Completed);
            ReadSettings();

            if (this.UseTelegram)
            {
                BotClient = new TelegramBotClient(this.TelegramToken);
            }

            if (this.GoogleClientID != "")
            {
                credential = GoogleAuthenticate();
            }

            TimeSpan ts = dateTimePicker1.Value.TimeOfDay;
            SetUpTimer(ts);

            //BotClient.OnMessage += Bot_OnMessage;
            //BotClient.StartReceiving();
        }

        private void SetUpTimer(TimeSpan alertTime)
        {
            DateTime targetDateTime = DateTime.Today.Add(alertTime);
            DateTime current = DateTime.Now;
           
            if (targetDateTime < current)
            {              
                Debug.WriteLine("Schedule for tomorrow");
                targetDateTime = targetDateTime.AddDays(1);
            }

            TimeSpan timeToGo = targetDateTime - current;

            if (this.timer != null)
            {
                this.timer.Dispose();
            }

            this.timer = new System.Threading.Timer(x =>
            {
                this.Invoke(new Action(() => performBackup()));
            }, null, timeToGo, TimeSpan.FromHours(24));
        }


        void Bot_OnMessage(object sender, Telegram.Bot.Args.MessageEventArgs e)
        {
            if (e.Message.Text != null)
            {
                Debug.WriteLine (e.Message.Chat.Id);

               // await botClient.SendTextMessageAsync(
                //  chatId: e.Message.Chat,
                 // text: "You said:\n" + e.Message.Text
              //  );
            }
        }

        private void ReadSettings()
        {
            this.DataBasePath = Settings.Default["dbpath"].ToString();
            this.BackupPath = Settings.Default["backuppath"].ToString();
            this.ArchivePassword = Settings.Default["archivepassword"].ToString();
            this.GDFolder = Settings.Default["gdfolder"].ToString();
            this.BackupTime = (DateTime)Settings.Default["backuptime"];
            this.GoogleClientID = Settings.Default["gid"].ToString();
            this.GoogleSecret = Settings.Default["gsecret"].ToString();
            this.TelegramChannelID = Settings.Default["tchid"].ToString();
            this.UseTelegram = (Boolean)Settings.Default["usetelegram"];
            this.TelegramToken = Settings.Default["telegramtoken"].ToString();
            this.BackupsCount = (int)Settings.Default["backupscount"];
            textBox1.Text = this.DataBasePath;
            textBox2.Text = this.BackupPath;
            textBox3.Text = this.ArchivePassword;
            textBox4.Text = this.GDFolder;
            textBox5.Text = this.GoogleClientID;
            textBox6.Text = this.GoogleSecret;
            textBox7.Text = this.TelegramToken;
            textBox8.Text = this.TelegramChannelID;
            checkBox1.Checked = this.UseTelegram;
            label10.Enabled = this.UseTelegram;
            textBox7.Enabled = this.UseTelegram;
            textBox8.Enabled = this.UseTelegram;
            label11.Enabled = this.UseTelegram;            
            dateTimePicker1.Value = this.BackupTime;
            numericUpDown1.Value = this.BackupsCount;
        }

        private void SaveSettings()
        {
            this.DataBasePath = textBox1.Text;
            this.BackupPath = textBox2.Text;
            this.ArchivePassword = textBox3.Text;
            this.GDFolder = textBox4.Text;
            this.BackupTime = dateTimePicker1.Value;
            this.GoogleClientID = textBox5.Text;
            this.GoogleSecret = textBox6.Text;
            this.UseTelegram = checkBox1.Checked;
            this.TelegramToken = textBox7.Text;
            this.TelegramChannelID = textBox8.Text;
            this.BackupsCount = (int)numericUpDown1.Value;
            Settings.Default["dbpath"] = this.DataBasePath;
            Settings.Default["backuppath"] = this.BackupPath;
            Settings.Default["archivepassword"] = this.ArchivePassword;
            Settings.Default["gdfolder"] = this.GDFolder;
            Settings.Default["backuptime"] = this.BackupTime;
            Settings.Default["gid"] = this.GoogleClientID;
            Settings.Default["gsecret"] = this.GoogleSecret;
            Settings.Default["usetelegram"] = this.UseTelegram;
            Settings.Default["tchid"] = this.TelegramChannelID;
            Settings.Default["telegramtoken"] = this.TelegramToken;
            Settings.Default["backupscount"] = this.BackupsCount;
            Settings.Default.Save();
        }

        private void HideME()
        {
            this.Visible = false;
            this.ShowInTaskbar = false;
            notifyIcon1.Visible = true;
        }

        private void ShowME()
        {
            this.Visible = true;
            this.ShowInTaskbar = true;
            notifyIcon1.Visible = false;
        }

        private UserCredential GoogleAuthenticate()
        {
            ClientSecrets secrets = new ClientSecrets()
            {
                ClientId = GoogleClientID,
                ClientSecret = GoogleSecret
            };

            // The file token.json stores the user's access and refresh tokens, and is created
            // automatically when the authorization flow completes for the first time.
            string credPath = "token.json";
            credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(credPath, true)).Result;
            //MessageBox.Show("Credential file saved to: " + credPath);
            return credential;
        }

        void Worker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            MethodInvoker m = new MethodInvoker(() => progressBar1.Value = 0);
            progressBar1.Invoke(m);
            String FileName = textBox2.Text + "\\" + (String)e.Result;
            label7.Text = "Загружаем в Google...";
            uploader.RunWorkerAsync(argument: FileName);
        }

        void Uploader_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            MethodInvoker m = new MethodInvoker(() => progressBar1.Value = 0);
            progressBar1.Invoke(m);
            label7.Text = "Ожидаем";
            RemoveBackups(this.BackupsCount);
            System.IO.File.Delete((String)e.Result);
            SendTelegramMessage("Бекап успешно загружен на Google диск");
            button5.Enabled = true;
        }

        private void Zip_SaveProgress(object sender, Zip.SaveProgressEventArgs e)
        {
            if (e.BytesTransferred > 0 && e.TotalBytesToTransfer > 0)
            {
                int progress = (int)Math.Floor((decimal)((e.BytesTransferred * 100) / e.TotalBytesToTransfer));
                MethodInvoker m = new MethodInvoker(() => progressBar1.Value = progress);
                progressBar1.Invoke(m);
            }
        }

        void Worker_doWork(object sender, DoWorkEventArgs e)
        {
            String FileName = (String)e.Argument;
            using (Zip.ZipFile zip = new Zip.ZipFile())
            {
                zip.UseUnicodeAsNecessary = true;
                if (this.ArchivePassword != "")
                {
                    zip.Password = this.ArchivePassword;
                }
                zip.AddDirectory(this.DataBasePath);
                zip.SaveProgress += Zip_SaveProgress;
                
                //FileName = "Backup_" + System.DateTime.Now.ToString("dd_MM_yyyy") + ".zip";
                zip.Save(this.BackupPath + "\\" + FileName);
            }
            e.Result = FileName;
        }

        void Uploader_doWork(object sender, DoWorkEventArgs e)
        {
            String FileName = (String)e.Argument;
            UploadToGoogle(FileName);
            e.Result = FileName;
        }

        private string GetIdByFolderName(String FolderName)
        {
            String Result = "";

            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            FilesResource.ListRequest listRequest = service.Files.List();
            listRequest.PageSize = 10;
            listRequest.Q = "mimeType = 'application/vnd.google-apps.folder' and name = '" + FolderName + "'";
            listRequest.Fields = "nextPageToken, files(id, name)";

            IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute()
            .Files;
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {   
                    if (file.Name == FolderName)
                    {
                        Result = file.Id;
                        break;
                    }                  
                }
            }
            return Result;

        }

        private void UploadToGoogle(String SourceFile)
        {            

            var service = new DriveService(new BaseClientService.Initializer()
            {

                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            String FolderId = GetIdByFolderName(textBox4.Text);
            if (FolderId == "")
            {
                return;
            }
            //MessageBox.Show(FolderId);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File();
            fileMetadata.MimeType = "application/zip";
            fileMetadata.Name = Path.GetFileName(SourceFile);
            fileMetadata.Parents = new List<string> { FolderId };
            FilesResource.CreateMediaUpload request;
            TotalBytesToUpload = new System.IO.FileInfo(SourceFile).Length;               
            using (var stream = new System.IO.FileStream(SourceFile, System.IO.FileMode.Open))
            {
                request = service.Files.Create(fileMetadata, stream, "application/zip");
                request.Fields = "id";
                request.ProgressChanged += Request_ProgressChanged;
                var progress = request.Upload();                
                if (progress.Exception != null)
                {
                    MessageBox.Show(progress.Exception.Message.ToString());
                }

            }

            var file = request.ResponseBody;
            //MessageBox.Show(file.Id);

        }

        private void Request_ProgressChanged(Google.Apis.Upload.IUploadProgress obj)
        {
            int percent = (int)(1.0f * obj.BytesSent / TotalBytesToUpload * 100);          
            Debug.WriteLine(obj.Status + " " + TotalBytesToUpload + " " + obj.BytesSent + " " + percent);
            MethodInvoker m = new MethodInvoker(() => progressBar1.Value = percent);
            progressBar1.Invoke(m);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.HideME();
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.ShowME();
        }

        private void ZipFile(String FileName)
        {

        }

        private void Form1_SizeChanged(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.HideME();
        }

        private void выходToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }



        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                textBox1.Text = Path.GetDirectoryName(openFileDialog1.FileName);
            }
        }

        private void performBackup()
        {
            button5.Enabled = false;
            SendTelegramMessage("Снятие бекапа началось");
            //********** Trying to make zip ***********
            String FileName = "Backup_" + System.DateTime.Now.ToString("dd_MM_yyyy_HH_mm_ss") + ".zip";
            label7.Text = "Создание архива...";
            progressBar1.Value = 0;
            worker.RunWorkerAsync(argument: FileName);
            //*****************************************
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(textBox1.Text))
            {
                MessageBox.Show("Не найдена директория с базой 1С", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!Directory.Exists(textBox2.Text))
            {
                MessageBox.Show("Не найдена директория для сохранения бекапов", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (textBox4.Text == "")
            {
                MessageBox.Show("Не указана директория на гугл диске для сохранения бекапов", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
           

            if (textBox5.Text == "")
            {
                MessageBox.Show("Не указан Google Client ID", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (textBox6.Text == "")
            {
                MessageBox.Show("Не указан Google Secret", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (checkBox1.Checked && textBox7.Text == "")
            {
                MessageBox.Show("Не указан Telegeam Token", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (checkBox1.Checked && textBox8.Text == "")
            {
                MessageBox.Show("Не указан Telegeam Channel ID", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.GoogleClientID = textBox5.Text;
            this.GoogleSecret = textBox6.Text;

            credential = GoogleAuthenticate();

            if (GetIdByFolderName(textBox4.Text) == "")
            {
                MessageBox.Show("Указана несуществующая директория на гугл диске", "Ошибка сохранения конфигурации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
           

            SaveSettings();

            TimeSpan ts = dateTimePicker1.Value.TimeOfDay;
            SetUpTimer(ts);            
        }

        private void folderBrowserDialog1_HelpRequest(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                textBox2.Text = folderDialog.SelectedPath;
            }
        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {
           
        }

        private void button5_Click(object sender, EventArgs e)
        {
            //RemoveBackups(FilesToLeave);
            performBackup();
        }

        private void RemoveBackups(int amount)
        {

            String FolderId = GetIdByFolderName(this.GDFolder);
            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            FilesResource.ListRequest listRequest = service.Files.List();
            FilesResource.DeleteRequest gdRequest;
            listRequest.PageSize = 10;
            listRequest.Q = "mimeType != 'application/vnd.google-apps.folder' and '" + FolderId + "' in parents";

            listRequest.Fields = "nextPageToken, files(id, name)";

            IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute()
            .Files;
            if (files != null && files.Count > 0)
            {
                int i = 0;
                foreach (var file in files)
                {
                    i++;
                    Debug.WriteLine(file.Name);
                    if (i > amount)
                    {
                        Debug.WriteLine("Delete...");
                        gdRequest = service.Files.Delete(file.Id);
                        gdRequest.Execute();
                    }
                    
                }
            }
           

        }

        private void SendTelegramMessage(String message)
        {
            if (this.UseTelegram)
            {
                BotClient.SendTextMessageAsync(chatId: this.TelegramChannelID, text: message);
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            textBox7.Enabled = checkBox1.Checked;
            label10.Enabled = checkBox1.Checked;
            label11.Enabled = checkBox1.Checked;
            textBox8.Enabled = checkBox1.Checked;
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {

        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            AboutBox AboutForm = new AboutBox();
            AboutForm.Show();
        }
    }
}
