namespace FirewiseMail.App.Mail;

public sealed record CampaignProgress(
    int Sent,
    int Failed,
    int Pending,
    int TotalDistinct,
    string Message);

