namespace train_planner.Services;

public class PlkApiOptions
{
    public const string SectionName = "PlkApi";
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey  { get; set; } = string.Empty;
}
