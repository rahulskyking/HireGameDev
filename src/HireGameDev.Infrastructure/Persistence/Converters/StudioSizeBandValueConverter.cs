using HireGameDev.Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HireGameDev.Infrastructure.Persistence.Converters;

/// <summary>
/// Maps <see cref="StudioSizeBand"/> to exact PostgreSQL <c>studio_profiles.size_band</c> strings
/// (<c>'1-10'</c>, <c>'11-50'</c>, <c>'51-200'</c>, <c>'200+'</c>).
/// </summary>
public sealed class StudioSizeBandValueConverter : ValueConverter<StudioSizeBand, string>
{
    public StudioSizeBandValueConverter()
        : base(
            band => band.ToDbValue(),
            dbValue => StudioSizeBandExtensions.FromDbValue(dbValue))
    {
    }
}
