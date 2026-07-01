namespace HyperVGroupManager.App.Services;

public class EmailReportConfig
{
    // SMTP
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;

    // "None" | "STARTTLS" | "SSL"
    public string SmtpSecurity { get; set; } = "STARTTLS";
    public bool UseAuthentication { get; set; } = false;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    // E-Mail-Inhalt
    public string SenderAddress { get; set; } = "";
    public string SenderDisplayName { get; set; } = "Hyper-V Group Manager";
    public List<string> RecipientAddresses { get; set; } = new();
    public string BodyPrefix { get; set; } = "";

    // Aufgabenplanung
    public string TargetName { get; set; } = "";
    public string ScheduleTime { get; set; } = "08:00";
    public string TaskName { get; set; } = "HyperVGroupManager_UntaggedVMsReport";
}
