namespace FirewiseMail.App.Database;

public sealed record RecipientSummary(int TotalDistinct, int Pending, int AlreadyMailed);

