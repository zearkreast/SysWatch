using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using SysWatch.Config;

namespace SysWatch.Notify;

public sealed class EmailNotifier : INotifier
{
    public string Name => "E-posta";

    public bool IsEnabled(AppConfig cfg) =>
        cfg.Email.Enabled && cfg.Email.SmtpHost.Length > 0 && cfg.Email.From.Length > 0 && cfg.Email.To.Length > 0;

    public bool IsMinimal(AppConfig cfg) => cfg.Email.Minimal;

    public async Task SendAsync(string text, AppConfig cfg, CancellationToken ct)
    {
        var e = cfg.Email;
        var pgp = e.PgpPublicKeyPath.Length > 0;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(e.From));
        foreach (var to in e.To.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(MailboxAddress.Parse(to));

        // The subject is never encrypted, so it stays generic when PGP or minimal mode is on.
        var firstLine = text.Split('\n')[0];
        message.Subject = pgp || e.Minimal ? "SysWatch bildirimi" : firstLine;

        var inner = new TextPart("plain") { Text = text, ContentTransferEncoding = ContentEncoding.Base64 };
        message.Body = pgp ? Pgp.Encrypt(inner, e.PgpPublicKeyPath) : inner;

        using var smtp = new SmtpClient { Timeout = 20000 };
        await smtp.ConnectAsync(e.SmtpHost, e.Port, SecureSocketOptions.Auto, ct).ConfigureAwait(false);
        if (e.Username.Length > 0)
            await smtp.AuthenticateAsync(e.Username, e.Password, ct).ConfigureAwait(false);
        await smtp.SendAsync(FormatOptions.Default, new MailKit.SendRequest(message), ct).ConfigureAwait(false);
        await smtp.DisconnectAsync(true, ct).ConfigureAwait(false);
    }
}

// PGP/MIME (RFC 3156) built directly on BouncyCastle so no GnuPG install is needed.
public static class Pgp
{
    public static MimeEntity Encrypt(MimeEntity inner, string publicKeyPath)
    {
        byte[] plain;
        using (var ms = new MemoryStream())
        {
            inner.WriteTo(ms);
            plain = ms.ToArray();
        }

        var key = ReadEncryptionKey(publicKeyPath);
        var armored = new MemoryStream();
        using (var armor = new ArmoredOutputStream(armored))
        {
            var encGen = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, true, new SecureRandom());
            encGen.AddMethod(key);
            using var encOut = encGen.Open(armor, new byte[1 << 14]);
            var compGen = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);
            using var compOut = compGen.Open(encOut);
            var litGen = new PgpLiteralDataGenerator();
            using var litOut = litGen.Open(compOut, PgpLiteralData.Binary, "message.eml", plain.Length, DateTime.UtcNow);
            litOut.Write(plain, 0, plain.Length);
        }

        var multipart = new Multipart("encrypted");
        multipart.ContentType.Parameters.Add("protocol", "application/pgp-encrypted");
        multipart.Add(new MimePart("application", "pgp-encrypted")
        {
            Content = new MimeContent(new MemoryStream("Version: 1\r\n"u8.ToArray())),
            ContentTransferEncoding = ContentEncoding.SevenBit,
        });
        multipart.Add(new MimePart("application", "octet-stream")
        {
            Content = new MimeContent(new MemoryStream(armored.ToArray())),
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline) { FileName = "encrypted.asc" },
            ContentTransferEncoding = ContentEncoding.SevenBit,
        });
        return multipart;
    }

    public static PgpPublicKey ReadEncryptionKey(string path)
    {
        using var file = File.OpenRead(path);
        var bundle = new PgpPublicKeyRingBundle(PgpUtilities.GetDecoderStream(file));
        var candidates = bundle.GetKeyRings()
            .SelectMany(r => r.GetPublicKeys())
            .Where(k => k.IsEncryptionKey && !k.HasRevocation() &&
                        (k.GetValidSeconds() == 0 || k.CreationTime.AddSeconds(k.GetValidSeconds()) > DateTime.UtcNow))
            .ToList();
        return candidates.FirstOrDefault(k => !k.IsMasterKey) ?? candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("Bu dosyada geçerli bir PGP şifreleme anahtarı yok.");
    }
}
