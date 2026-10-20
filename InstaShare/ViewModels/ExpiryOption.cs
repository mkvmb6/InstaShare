using InstaShare.Services;

namespace InstaShare.ViewModels
{
    internal sealed record ExpiryOption(TimeSpan Value)
    {
        public static IReadOnlyList<ExpiryOption> All { get; } =
            // Must match ALLOWED_EXPIRY_HOURS in Api/src/shares.ts; the server rejects anything else.
            [new(TimeSpan.FromHours(1)), new(TimeSpan.FromHours(6)), new(TimeSpan.FromHours(12)),
             new(TimeSpan.FromDays(1)), new(TimeSpan.FromDays(2)), new(TimeSpan.FromDays(7))];

        public string Label => Format.Expiry(Value);

        /// <summary>
        /// The option nearest to <paramref name="hours"/>, so a hand-edited settings file still picks something sensible.
        /// </summary>
        public static ExpiryOption Closest(double hours) => All.MinBy(o => Math.Abs(o.Value.TotalHours - hours))!;

        public override string ToString() => Label;
    }
}
