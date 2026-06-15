namespace ApprovalWorkflow.Web.Config;

public class AppSettings
{
    public DataverseConfig   Dataverse  { get; set; } = new();
    public LogicAppsConfig   LogicApps  { get; set; } = new();
    public AppConfig         App        { get; set; } = new();
    public List<DemoUser>    DemoUsers  { get; set; } = new();
}

public class DataverseConfig
{
    public string Url          { get; set; } = string.Empty;
    public string ClientId     { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TenantId     { get; set; } = string.Empty;
}

public class LogicAppsConfig
{
    public string InitiateUrl      { get; set; } = string.Empty;  // LA-1 trigger URL
    public string CallbackRelayUrl { get; set; } = string.Empty;  // LA-2 trigger URL
    public string ResetHandlerUrl  { get; set; } = string.Empty;  // LA-5 trigger URL
    public string SubscriptionId   { get; set; } = string.Empty;
    public string ResourceGroup    { get; set; } = string.Empty;
    public string LA1Name          { get; set; } = "la-approval-initiate";
}

public class AppConfig
{
    public string BaseUrl            { get; set; } = string.Empty;
    public int    DefaultExpiryHours { get; set; } = 72;
    /// <summary>
    /// When true, ExpiryHours/ReminderHours values are treated as minutes.
    /// Set to true in appsettings for demo; false for production.
    /// </summary>
    public bool   DemoMode          { get; set; } = false;
}

public class DemoUser
{
    public string Email { get; set; } = string.Empty;
    public string Name  { get; set; } = string.Empty;
    public string Type  { get; set; } = string.Empty;  // "Internal" | "External"
}
