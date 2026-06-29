# SharpEmailMcp

A standard MCP (Model Context Protocol) email server built on **ModelContextProtocol SDK** + **MailKit**.

Supports plain text / HTML body, file attachments (absolute paths), and CID inline images.

---

## 1. Get & Publish

```bash
# Clone the repository
git clone https://github.com/Axvser/SharpEmailMcp.git

# Publish to a custom MCP installation path
dotnet publish -c Release -o /path/to/your/
```

Published layout:
```
/path/to/your/.evn/mcp/dotnet/sharp-email-mcp/
├── SharpEmailMcp.dll
├── SharpEmailMcp.exe
├── SharpEmailMcp.runtimeconfig.json
├── MailKit.dll
├── MimeKit.dll
├── ModelContextProtocol.dll
└── ...
```

---

## 2. Startup Parameters

CLI arguments take precedence over environment variables. Run with `--help` to see all options.

| Argument | Env Variable | Default | Description |
|----------|-------------|---------|-------------|
| `--smtp-host`, `-h` | `SMTP_HOST` | `smtp.qq.com` | SMTP server address |
| `--smtp-port`, `-p` | `SMTP_PORT` | `465` | SMTP port |
| `--smtp-user`, `-u` | `SMTP_USER` | — | Email account |
| `--smtp-pass`, `-s` | `SMTP_PASS` | — | Password or app auth code |
| `--sender-name`, `-n` | `SENDER_NAME` | same as user | Sender display name |

```bash
# CLI arguments
dotnet run -- --smtp-user your@email.com --smtp-pass your-auth-code

# Environment variables
set SMTP_USER=your@email.com
set SMTP_PASS=your-auth-code
dotnet run
```

---

## 3. MCP Tool: `send_email`

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `to` | `string[]` | ✅ | Recipient email addresses |
| `subject` | `string` | ✅ | Email subject |
| `body` | `string` | ✅ | Email body (plain text or HTML) |
| `cc` | `string[]` | ❌ | CC recipients |
| `bcc` | `string[]` | ❌ | BCC recipients |
| `is_html` | `boolean` | ❌ | Whether body is HTML |
| `attachments` | `string[]` | ❌ | **Absolute** file paths to attach |
| `inline_images` | `string[]` | ❌ | CID inline images: `["absPath|cid", ...]` |

### Path Notes

- All paths must be **absolute**.
- `inline_images` format: `["C:\\logo.png|my-cid", "C:\\banner.jpg|banner"]`
- Reference in HTML: `<img src='cid:my-cid'>`

### Example Invocations

```json
// Plain text
{ "to": ["user@example.com"], "subject": "Hello", "body": "Hi there" }

// HTML + attachments + CID image
{
  "to": ["user@example.com"],
  "subject": "Report",
  "body": "<img src='cid:logo'><p>See attached.</p>",
  "is_html": true,
  "attachments": ["C:\\report.pdf"],
  "inline_images": ["C:\\logo.png|logo"]
}
```

---

## 4. Usage

### 4.1 Raw ModelContextProtocol SDK

Connect directly via `McpClient` with no framework dependency.

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Transport;

// Start SharpEmailMcp as a stdio subprocess
await using var client = await McpClient.CreateAsync(
    new StdioClientTransport(new()
    {
        Command = "dotnet",
        Arguments = ["run", "--project", @"E:\Projects\SharpEmailMcp", "--no-build"],
        EnvironmentVariables = new()
        {
            ["SMTP_HOST"] = "smtp.example.com",
            ["SMTP_PORT"] = "465",
            ["SMTP_USER"] = "YOUR_EMAIL",
            ["SMTP_PASS"] = "YOUR_AUTH_CODE",
        }
    }),
    new McpClientOptions { ClientInfo = new() { Name = "MyApp", Version = "1.0.0" } }
);

// Send email with attachments
await client.CallToolAsync("send_email", new()
{
    ["to"] = "recipient@example.com",
    ["cc"] = new[] { "cc@example.com" },
    ["subject"] = "Report with attachments",
    ["body"] = "Please find the attached files.",
    ["attachments"] = new[]
    {
        @"C:\Reports\document1.pdf",
        @"C:\Reports\document2.docx"
    }
});

// Send HTML with CID inline image
await client.CallToolAsync("send_email", new()
{
    ["to"] = "recipient@example.com",
    ["subject"] = "Banner image",
    ["body"] = """
        <html><body>
          <h2>Preview</h2>
          <img src='cid:banner' width='600'/>
        </body></html>
        """,
    ["is_html"] = true,
    ["inline_images"] = new[]
    {
        @"C:\Images\banner.png|banner"
    }
});
```

> Works in any .NET project — just add the `ModelContextProtocol` NuGet package.

### 4.2 VeloxDev.Core.Extension (McpScope)

If your project already uses **VeloxDev.Core.Extension**, load SharpEmailMcp via `McpScope` without manually managing `McpClient`.

**Prerequisite:** Publish SharpEmailMcp to the McpScope installation directory first:

```bash
dotnet publish -c Release -o .evn/mcp/dotnet/sharp-email-mcp
```

**Configuration & invocation:**

```csharp
using Microsoft.Extensions.AI;
using VeloxDev.AI.MCP;

var scope = new McpScope();

var tools = await scope.LoadAsync([
    new McpServerConfiguration
    {
        Name = "email",
        RunMode = McpServerRunMode.Dotnet,
        NpmPackage = "sharp-email-mcp",
        ServerModulePath = "SharpEmailMcp.dll",
        // Pass SMTP credentials via CLI arguments — never hardcode
        ServerArguments =
        [
            "--smtp-user", Environment.GetEnvironmentVariable("SMTP_USER")!,
            "--smtp-pass", Environment.GetEnvironmentVariable("SMTP_PASS")!,
        ],
    }
]);

// Find the tool and invoke it
var sendEmail = tools.First(t => t.Name == "send_email");
var func = (AIFunction)sendEmail;

var result = await func.InvokeAsync(new AIFunctionArguments
{
    ["to"] = new[] { "user@example.com" },
    ["subject"] = "Hello from McpScope",
    ["body"] = "This email was sent via VeloxDev.Core.Extension!",
});
Console.WriteLine(result);
```

**Unit test reference** (see `VeloxDev.Core.Extension.Test` for the full test):

```csharp
using Microsoft.Extensions.AI;
using VeloxDev.AI.MCP;

[TestClass]
public class EmailTests
{
    [TestMethod]
    [TestCategory("McpDotnet")]
    public async Task SendEmail_WithAttachments_Success()
    {
        var smtpUser = Environment.GetEnvironmentVariable("TEST_SMTP_USER")
            ?? throw new AssertInconclusiveException("Set TEST_SMTP_USER");
        var smtpPass = Environment.GetEnvironmentVariable("TEST_SMTP_PASS")
            ?? throw new AssertInconclusiveException("Set TEST_SMTP_PASS");
        var to = Environment.GetEnvironmentVariable("TEST_TO")
            ?? throw new AssertInconclusiveException("Set TEST_TO");

        var scope = new McpScope();
        var tools = await scope.LoadAsync([
            new McpServerConfiguration
            {
                Name = "email",
                RunMode = McpServerRunMode.Dotnet,
                NpmPackage = "sharp-email-mcp",
                ServerModulePath = "SharpEmailMcp.dll",
                ServerArguments =
                [
                    "--smtp-user", smtpUser,
                    "--smtp-pass", smtpPass,
                ],
            }
        ]);

        var func = (AIFunction)tools.First(t => t.Name == "send_email");
        var result = await func.InvokeAsync(new AIFunctionArguments
        {
            ["to"] = to.Split(','),
            ["subject"] = $"[Test] McpScope — {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            ["body"] = "Test from VeloxDev.Core.Extension McpScope.",
        });

        StringAssert.Contains(result?.ToString() ?? "", "Email sent successfully");
    }
}
```

> `McpScope.LoadAsync()` returns `AITool[]` where each item is actually an `AIFunction` — cast and call `InvokeAsync` directly.

---

## Project Structure

```
SharpEmailMcp/
├── SharpEmailMcp.csproj   ← Dependencies: MailKit + ModelContextProtocol
├── Program.cs              ← Entry point with CLI arg parsing
├── Tools/
│   └── EmailTool.cs        ← send_email implementation (MailKit)
└── README.md
```

