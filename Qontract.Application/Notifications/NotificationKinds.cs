namespace Qontract.Application.Notifications;

public static class NotificationKinds
{
    public const string NoticeDeadline = "noticeDeadline";
    public const string EndDate = "endDate";
    public const string MinimumTerm = "minimumTerm";
    public const string PriceIncrease = "priceIncrease";
    public const string InsuranceReview = "insuranceReview";
    public const string MissingData = "missingData";
    public const string AdminWithout2fa = "adminWithout2fa";

    public static readonly IReadOnlyList<string> All =
    [
        NoticeDeadline, EndDate, MinimumTerm, PriceIncrease, InsuranceReview, MissingData, AdminWithout2fa
    ];
}
