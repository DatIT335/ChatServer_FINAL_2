using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChatApp.Shared
{
    // --- 1. CÁC LOẠI GÓI TIN ---
    public enum PacketType
    {
        Auth,           // Đăng nhập
        Message,        // Tin nhắn văn bản
        Video,          // Hình ảnh Video Call
        File,           // Truyền File
        KeyExchange,    // Trao đổi khóa Diffie-Hellman
        Error           // Báo lỗi
    }

    // --- 2. CẤU TRÚC DỮ LIỆU (DTO) ---
    public class  DataPacket
    {
        public PacketType Type { get; set; }

        // Người gửi
        public string Sender { get; set; } = "";

        // [MỚI] Người nhận: 
        // - Nếu để trống hoặc null: Gửi cho tất cả (Broadcast)
        // - Nếu có tên: Gửi riêng cho người đó (Private)
        public string Recipient { get; set; } = "";

        // Dùng cho Auth (Mật khẩu)
        public string Password { get; set; } = "";

        // Dùng cho File Transfer (Tên file)
        public string FileName { get; set; } = "";

        // Payload chính (Chứa Text mã hóa, Ảnh Video, File, hoặc Public Key)
        public byte[]? Data { get; set; }

        // Vector khởi tạo (IV) bắt buộc cho AES
        public byte[]? IV { get; set; }
    }

    // --- 3. BỘ MÃ HÓA AES (CƠ CHẾ HYBRID: DH + DEFAULT) ---
    public static class SimpleAES
    {
        // Khóa dự phòng (Dùng cho Chat nhóm, File và Video công khai)
        // Đảm bảo ai cũng có thể giải mã được nếu không có khóa riêng tư
        public static readonly byte[] DefaultKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");

        // --- A. MÃ HÓA CHUỖI (TEXT) ---
        public static byte[] EncryptString(string text, byte[] key, out byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                // Nếu key null thì dùng DefaultKey
                aes.Key = key ?? DefaultKey;
                aes.GenerateIV();
                iv = aes.IV;
                aes.Padding = PaddingMode.PKCS7; // Chuẩn Padding quốc tế

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs)) { sw.Write(text); }
                    return ms.ToArray();
                }
            }
        }

        // --- GIẢI MÃ CHUỖI (CÓ CƠ CHẾ TỰ SỬA LỖI PADDING) ---
        public static string DecryptString(byte[] cipherText, byte[] key, byte[] iv)
        {
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key ?? DefaultKey;
                    aes.IV = iv;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream(cipherText))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                // QUAN TRỌNG: Nếu giải mã bằng khóa DH thất bại (thường do chat nhóm)
                // -> Thử giải mã lại bằng DefaultKey
                if (key != DefaultKey)
                {
                    return DecryptString(cipherText, DefaultKey, iv);
                }

                return "[Tin nhắn lỗi mã hóa - Không thể đọc]";
            }
        }

        // --- B. MÃ HÓA FILE / VIDEO (BYTE ARRAY) ---
        public static byte[] EncryptBytes(byte[] originalData, byte[] key, out byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key ?? DefaultKey;
                aes.GenerateIV();
                iv = aes.IV;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                {
                    return encryptor.TransformFinalBlock(originalData, 0, originalData.Length);
                }
            }
        }

        public static byte[] DecryptBytes(byte[] cipherData, byte[] key, byte[] iv)
        {
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key ?? DefaultKey;
                    aes.IV = iv;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        return decryptor.TransformFinalBlock(cipherData, 0, cipherData.Length);
                    }
                }
            }
            catch
            {
                // Thử lại với DefaultKey nếu lỗi (Cơ chế fallback)
                if (key != DefaultKey) return DecryptBytes(cipherData, DefaultKey, iv);

                // Trả về rỗng nếu bó tay (để không crash app)
                return new byte[0];
            }
        }
    }
}