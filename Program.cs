using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpEmailMcp.Tools;

Console.Error.WriteLine("[SharpEmailMcp] Starting...");

// ── 1. 读取配置：CLI 参数优先，其次环境变量 ──
var smtpHost = GetOptOrEnv("--smtp-host", "SMTP_HOST", "smtp.qq.com");
var smtpPortStr = GetOptOrEnv("--smtp-port", "SMTP_PORT", "465");
var smtpUser = GetOptOrEnv("--smtp-user", "SMTP_USER", "");
var smtpPass = GetOptOrEnv("--smtp-pass", "SMTP_PASS", "");
var senderName = GetOptOrEnv("--sender-name", "SENDER_NAME", null);

int.TryParse(smtpPortStr, out int smtpPort);

SetEnv("SMTP_HOST", smtpHost);
SetEnv("SMTP_PORT", smtpPort.ToString());
SetEnv("SMTP_USER", smtpUser);
SetEnv("SMTP_PASS", smtpPass);
SetEnv("SENDER_NAME", senderName);

// ── 2. 启动 MCP 服务器（长期运行，直到 stdin 关闭） ──
await StartServer();

static string GetOptOrEnv(string optName, string envKey, string? fallback)
{
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], optName, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return Environment.GetEnvironmentVariable(envKey) ?? fallback ?? "";
}

static async Task StartServer()
{
    var transport = new StdioServerTransport("SharpEmailMcp");
    var options = new McpServerOptions
    {
        ServerInfo = new Implementation { Name = "SharpEmailMcp", Version = "1.0.0" },
        Capabilities = new ServerCapabilities { Tools = new() },
        ToolCollection =
        [
            McpServerTool.Create(
                async (string[] to, string subject, string body,
                       string[]? cc = null, string[]? bcc = null,
                       bool? is_html = null,
                       string[]? attachments = null,
                       string[]? inline_images = null) =>
                    await EmailTool.SendEmail(to, subject, body,
                        cc, bcc, is_html, attachments, inline_images),
                new McpServerToolCreateOptions
                {
                    Name = "send_email",
                    Title = "Send email",
                    Description = "Send email via SMTP. Supports plain text / HTML, " +
                        "file attachments (absolute paths), and CID inline images. " +
                        "inline_images format: [\"absPath|cid\", ...]. " +
                        "Reference in HTML as <img src='cid:xxx'>.",
                }),
        ],
    };

    await using var server = McpServer.Create(transport, options);
    Console.Error.WriteLine("[SharpEmailMcp] Ready. Use tool 'send_email'.");
    await server.RunAsync();
}

static void SetEnv(string key, string? val)
{
    if (!string.IsNullOrEmpty(val))
        Environment.SetEnvironmentVariable(key, val);
}

