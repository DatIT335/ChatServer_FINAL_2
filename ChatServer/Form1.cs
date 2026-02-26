using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq; // Cần dùng LINQ để tìm người nhận
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using ChatApp.Shared; // Bắt buộc: Namespace từ Models.cs
using Microsoft.Data.Sqlite;

namespace ChatServer
{
    public partial class Form1 : Form
    {
        // --- 1. GIAO DIỆN SERVER ---
        private Button btnStart, btnStop;
        private ListView lvClients;
        private RichTextBox txtLog;
        private Label lblStatus;

        // --- 2. HỆ THỐNG MẠNG ---
        private TcpListener listener;
        public List<ClientHandler> clients = new List<ClientHandler>(); // Public để truy cập từ Handler
        private bool isRunning = false;

        public Form1()
        {
            SetupUI();
        }
        private void InitializeDatabase()
        {
            string dbPath = Path.Combine(Application.StartupPath, "ChatDB.sqlite");
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();

                // Đã sửa: Tạo cả bảng Accounts và bảng ServerLogs cùng lúc
                command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Accounts (
                Username TEXT PRIMARY KEY,
                Password TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ServerLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp DATETIME,
                Content TEXT
            );";
                command.ExecuteNonQuery();

                // Kiểm tra nếu chưa có tài khoản nào thì tạo mặc định
                command.CommandText = "SELECT COUNT(*) FROM Accounts";
                long count = (long)command.ExecuteScalar();
                if (count == 0)
                {
                    command.CommandText = @"
                INSERT INTO Accounts (Username, Password) VALUES ('admin', '123456');
                INSERT INTO Accounts (Username, Password) VALUES ('user1', 'password');
                INSERT INTO Accounts (Username, Password) VALUES ('sinhvien', 'dhcn');
                INSERT INTO Accounts (Username, Password) VALUES ('vanhien', '123');
                INSERT INTO Accounts (Username, Password) VALUES ('huy', '1235');
            ";
                    command.ExecuteNonQuery();
                }
            }
        }
        // --- 3. THIẾT KẾ GIAO DIỆN (CODE THUẦN) ---
        private void SetupUI()
        {
            this.Text = "SERVER QUẢN LÝ (Routing: Private & Group)";
            this.Size = new Size(950, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // PANEL TOP
            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.WhiteSmoke };

            btnStart = new Button { Text = "BẬT SERVER", Location = new Point(15, 15), Width = 110, Height = 30, BackColor = Color.SteelBlue, ForeColor = Color.White, Cursor = Cursors.Hand };
            btnStop = new Button { Text = "TẮT SERVER", Location = new Point(135, 15), Width = 110, Height = 30, Enabled = false, BackColor = Color.Gray, ForeColor = Color.White, Cursor = Cursors.Hand };

            lblStatus = new Label { Text = "OFFLINE", Location = new Point(260, 22), AutoSize = true, Font = new Font("Arial", 10, FontStyle.Bold), ForeColor = Color.Red };

            btnStart.Click += (s, e) => StartServer();
            btnStop.Click += (s, e) => StopServer();
            pnlTop.Controls.AddRange(new Control[] { btnStart, btnStop, lblStatus });

            // LISTVIEW
            lvClients = new ListView { Dock = DockStyle.Left, Width = 300, View = View.Details, GridLines = true, FullRowSelect = true };
            lvClients.Columns.Add("Tên Client", 140);
            lvClients.Columns.Add("Thời gian vào", 140);

            // LOG
            txtLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.Black, ForeColor = Color.Lime, Font = new Font("Consolas", 10) };

            this.Controls.Add(txtLog);
            this.Controls.Add(lvClients);
            this.Controls.Add(pnlTop);
        }

        // --- 4. LOGIC KHỞI ĐỘNG ---
        private void StartServer()
        {
            try
            {
                // GỌI HÀM TẠO DATABASE TRƯỚC KHI BẬT LISTENER
                InitializeDatabase();

                listener = new TcpListener(IPAddress.Any, 9000);
                listener.Start();
                isRunning = true;

                btnStart.Enabled = false;
                btnStop.Enabled = true;
                btnStop.BackColor = Color.Crimson;

                lblStatus.Text = "ONLINE (Port 9000)";
                lblStatus.ForeColor = Color.Green;

                AddLog("Server đã khởi động.");

                new Thread(ListenLoop).Start();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi bật Server: " + ex.Message); }
        }

        private void StopServer()
        {
            isRunning = false;
            listener.Stop();
            foreach (var c in clients.ToArray()) c.Close();
            clients.Clear();
            lvClients.Items.Clear();

            btnStart.Enabled = true; btnStop.Enabled = false; btnStop.BackColor = Color.Gray;
            lblStatus.Text = "OFFLINE"; lblStatus.ForeColor = Color.Red;
            AddLog("Server đã tắt.");
        }

        private void ListenLoop()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient socket = listener.AcceptTcpClient();
                    ClientHandler handler = new ClientHandler(socket, this);
                    clients.Add(handler);
                }
                catch { }
            }
        }

        // --- 5. CÁC HÀM CẬP NHẬT GIAO DIỆN ---
        public void AddLog(string msg)
        {
            // 1. Vẫn hiển thị lên màn hình console đen của Server
            Invoke(new Action(() => {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                txtLog.ScrollToCaret();
            }));

            // 2. [MỚI] Ghi vào cơ sở dữ liệu SQLite
            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "ChatDB.sqlite");
                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();

                    command.CommandText = "INSERT INTO ServerLogs (Timestamp, Content) VALUES ($time, $content)";
                    // Dùng DateTime.Now để lấy chính xác giờ trên máy tính
                    command.Parameters.AddWithValue("$time", DateTime.Now);
                    command.Parameters.AddWithValue("$content", msg);

                    command.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public void UpdateList(string name, string time, bool add) => Invoke(new Action(() => {
            if (add)
            {
                var item = new ListViewItem(name);
                item.SubItems.Add(time);
                lvClients.Items.Add(item);
            }
            else
            {
                foreach (ListViewItem item in lvClients.Items)
                    if (item.Text == name) { lvClients.Items.Remove(item); break; }
            }
        }));

        // --- 6. HÀM ĐỊNH TUYẾN GÓI TIN (ROUTING) ---

        // A. Gửi cho TẤT CẢ (Chat Nhóm, File công khai)
        public void Broadcast(DataPacket p, ClientHandler sender)
        {
            foreach (var c in clients)
            {
                if (c != sender && c.IsAuthenticated) c.Send(p);
            }
        }

        // B. Gửi RIÊNG TƯ (Video Call 1-1, Chat mật)
        public void SendPrivate(string recipientName, DataPacket p)
        {
            // Tìm người nhận trong danh sách (Không phân biệt hoa thường)
            var target = clients.FirstOrDefault(c => c.Username.Equals(recipientName, StringComparison.OrdinalIgnoreCase));

            if (target != null && target.IsAuthenticated)
            {
                target.Send(p); // Chỉ gửi cho đúng người này
            }
        }

        public void RemoveClient(ClientHandler c)
        {
            clients.Remove(c);
            if (c.IsAuthenticated) UpdateList(c.Username, "", false);
        }
    }

    // --- 7. CLASS XỬ LÝ CLIENT ---
    public class ClientHandler
    {
        public TcpClient Socket;
        public Form1 Server;
        public string Username = "Unknown";
        public bool IsAuthenticated = false;

        private StreamReader reader;
        private StreamWriter writer;

        public ClientHandler(TcpClient s, Form1 f)
        {
            Socket = s; Server = f;
            var stream = s.GetStream();
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { AutoFlush = true };
            new Thread(Process).Start();
        }

        // Hàm kiểm tra đăng nhập từ file
        private bool CheckLogin(string user, string pass)
        {
            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "ChatDB.sqlite");
                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    // Sử dụng parameter để chống SQL Injection
                    command.CommandText = "SELECT COUNT(*) FROM Accounts WHERE Username = $user AND Password = $pass";
                    command.Parameters.AddWithValue("$user", user);
                    command.Parameters.AddWithValue("$pass", pass);

                    long count = (long)command.ExecuteScalar();
                    return count > 0;
                }
            }
            catch (Exception ex)
            {
                Server.AddLog("Lỗi Database: " + ex.Message);
            }
            return false;
        }

        void Process()
        {
            try
            {
                while (Socket.Connected)
                {
                    string json = reader.ReadLine();
                    if (json == null) break;

                    var packet = JsonSerializer.Deserialize<DataPacket>(json);
                    if (packet == null) continue;

                    // A. XỬ LÝ ĐĂNG NHẬP
                    // Kiểm tra xem gói tin DTO có phải là loại Auth (Đăng nhập)
                    if (packet.Type == PacketType.Auth)
                    {
                        // Gọi hàm CheckLogin truyền vào Sender (User) và Password từ DTO
                        if (CheckLogin(packet.Sender, packet.Password))
                        {
                            Username = packet.Sender;
                            IsAuthenticated = true;

                            Server.AddLog($"---> {Username} đăng nhập.");
                            Server.UpdateList(Username, DateTime.Now.ToString("HH:mm"), true);

                            // Gửi phản hồi OK
                            Send(new DataPacket { Type = PacketType.Auth, Sender = "Server" });
                        }
                        else
                        {
                            Send(new DataPacket { Type = PacketType.Error, Password = "Sai tài khoản hoặc mật khẩu!" });
                            break;
                        }
                    }
                    // B. XỬ LÝ CÁC GÓI TIN KHÁC
                    else if (IsAuthenticated)
                    {
                        // Log hoạt động (Trừ video để đỡ lag log)
                        if (packet.Type == PacketType.Message)
                        {
                            string msgContent = "";
                            try
                            {
                                // Yêu cầu Server giải mã nội dung tin nhắn bằng DefaultKey
                                msgContent = SimpleAES.DecryptString(packet.Data, SimpleAES.DefaultKey, packet.IV);
                            }
                            catch
                            {
                                // Nếu không giải mã được (do Client dùng khóa riêng Diffie-Hellman)
                                msgContent = "[Tin nhắn mã hóa riêng tư - Server không thể đọc]";
                            }

                            // Ghi log kèm theo nội dung tin nhắn
                            Server.AddLog($"Tin nhắn: {Username} -> {(string.IsNullOrEmpty(packet.Recipient) ? "All" : packet.Recipient)}: {msgContent}");
                        }
                        else if (packet.Type == PacketType.File) Server.AddLog($"File: {Username} gửi {packet.FileName}");

                        // --- LOGIC ĐỊNH TUYẾN QUAN TRỌNG ---
                        if (!string.IsNullOrEmpty(packet.Recipient))
                        {
                            // Nếu có người nhận cụ thể -> Gửi riêng (Video Call Private)
                            Server.SendPrivate(packet.Recipient, packet);
                        }
                        else
                        {
                            // Nếu không có người nhận -> Gửi hết (Broadcast)
                            Server.Broadcast(packet, this);
                        }
                    }
                }
            }
            catch { }

            Server.RemoveClient(this);
            if (IsAuthenticated) Server.AddLog($"<--- {Username} thoát.");
            Close();
        }

        public void Send(DataPacket p)
        {
            try { lock (writer) writer.WriteLine(JsonSerializer.Serialize(p)); } catch { }
        }

        public void Close() { try { Socket.Close(); } catch { } }
    }
}