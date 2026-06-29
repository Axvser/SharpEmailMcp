using MimeKit;
using MailKit.Net.Smtp;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace SharpEmailMcp.Tools;

/// <summary>
/// MCP 工具: send_email
/// 通过 MailKit SMTP 发送邮件，支持纯文本/HTML 正文、文件附件、CID 内嵌图片。
/// SMTP 配置从环境变量读取：SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SENDER_NAME。
/// 所有文件路径均使用绝对路径，不限制附件目录。
/// </summary>
[McpServerToolType]
public static class EmailTool
{
    /// <summary>
    /// 发送邮件。支持纯文本/HTML 正文、文件附件（绝对路径）、CID 内嵌图片。
    /// inline_images 格式：["绝对路径|cid1", "绝对路径|cid2", ...]
    /// HTML 中用 <img src='cid:xxx'> 引用内嵌图片。
    /// </summary>
    [McpServerTool(Name = "send_email")]
    [Description("Send email via SMTP. Supports plain text / HTML body, " +
        "file attachments (absolute paths), and CID inline images. " +
        "Format for inline_images: [\"absPath|cid\", ...]. " +
        "Reference inline images in HTML as <img src='cid:xxx'>.")]
    public static async Task<string> SendEmail(
        [Description("Recipient email addresses")] string[] to,
        [Description("Email subject")] string subject,
        [Description("Email body (plain text or HTML)")] string body,
        [Description("CC recipients")] string[]? cc = null,
        [Description("BCC recipients")] string[]? bcc = null,
        [Description("true = HTML body, false = plain text (default)")] bool? is_html = null,
        [Description("File attachments (absolute paths)")] string[]? attachments = null,
        [Description("Inline images: [\"absPath|cid\", ...]")] string[]? inline_images = null)
    {
        // 从环境变量读取 SMTP 配置
        var smtpHost = Env("SMTP_HOST", "smtp.qq.com");
        var smtpPort = int.Parse(Env("SMTP_PORT", "465"));
        var smtpUser = Env("SMTP_USER", "");
        var smtpPass = Env("SMTP_PASS", "");
        var senderName = Env("SENDER_NAME", smtpUser);
        var senderAddr = smtpUser;

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderAddr));
            foreach (var a in to) message.To.Add(new MailboxAddress("", a));
            if (cc is not null) foreach (var a in cc) message.Cc.Add(new MailboxAddress("", a));
            if (bcc is not null) foreach (var a in bcc) message.Bcc.Add(new MailboxAddress("", a));
            message.Subject = subject;

            var hasAttachments = attachments is { Length: > 0 };
            var hasInline = inline_images is { Length: > 0 };

            // 有附件或内嵌图片 → 使用 multipart
            if (hasAttachments || hasInline)
            {
                var mixed = new Multipart("mixed");

                // 正文（如果含内嵌图片则用 multipart/related 包装）
                if (hasInline)
                {
                    var related = new Multipart("related");
                    related.Add(new TextPart(is_html == true ? "html" : "plain")
                    {
                        Text = body,
                        ContentTransferEncoding = ContentEncoding.QuotedPrintable,
                    });

                    foreach (var item in inline_images!)
                    {
                        // 格式: "绝对路径|cid"
                        var parts = item.Split('|', 2);
                        if (parts.Length != 2)
                            return $"Error: Invalid inline_image format '{item}'. Expected 'path|cid'.";
                        var imgPath = parts[0];
                        var cid = parts[1];

                        if (!File.Exists(imgPath))
                            return $"Error: Inline image not found: {imgPath}";

                        var ext = Path.GetExtension(imgPath).TrimStart('.').ToLower();
                        var mimeSub = ext switch
                        {
                            "png" => "png", "jpg" or "jpeg" => "jpeg",
                            "gif" => "gif", "bmp" => "bmp",
                            "svg" => "svg+xml", "webp" => "webp",
                            _ => "png",
                        };
                        related.Add(new MimePart("image", mimeSub)
                        {
                            Content = new MimeContent(File.OpenRead(imgPath)),
                            ContentId = cid,
                            ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                            ContentTransferEncoding = ContentEncoding.Base64,
                        });
                    }
                    mixed.Add(related);
                }
                else
                {
                    mixed.Add(new TextPart(is_html == true ? "html" : "plain")
                    {
                        Text = body,
                        ContentTransferEncoding = ContentEncoding.QuotedPrintable,
                    });
                }

                // 文件附件（绝对路径）
                if (hasAttachments)
                {
                    foreach (var path in attachments!)
                    {
                        if (!File.Exists(path))
                            return $"Error: Attachment not found: {path}";
                        mixed.Add(new MimePart("application", "octet-stream")
                        {
                            Content = new MimeContent(File.OpenRead(path)),
                            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                            ContentTransferEncoding = ContentEncoding.Base64,
                            FileName = Path.GetFileName(path),
                        });
                    }
                }

                message.Body = mixed;
            }
            else
            {
                // 纯文本或纯 HTML，无附件
                message.Body = new TextPart(is_html == true ? "html" : "plain")
                {
                    Text = body,
                    ContentTransferEncoding = ContentEncoding.QuotedPrintable,
                };
            }

            // 通过 SMTP 发送
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(smtpHost, smtpPort, MailKit.Security.SecureSocketOptions.SslOnConnect);
            await smtp.AuthenticateAsync(smtpUser, smtpPass);
            var msgId = await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);
            return $"Email sent successfully! Message-ID: {msgId}";
        }
        catch (Exception ex)
        {
            return $"Failed to send email: {ex.Message}";
        }
    }

    /// <summary>读取环境变量，不存在时返回默认值。</summary>
    private static string Env(string key, string fallback)
        => Environment.GetEnvironmentVariable(key) ?? fallback;
}
