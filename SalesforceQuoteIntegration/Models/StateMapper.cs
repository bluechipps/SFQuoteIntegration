namespace SalesforceQuoteIntegration.Services;

/// <summary>
/// Converts Salesforce BillingState values into 2-letter USPS postal abbreviations
/// suitable for the EBS custstate varchar(2) field.
///
/// Salesforce may store either the full name ("Texas") or the abbreviation ("TX")
/// depending on whether State/Country picklists are enabled in the org, so this
/// mapper accepts both and normalizes to the 2-letter code.
/// </summary>
public static class StateMapper
{
    // Full name (upper-cased) -> 2-letter code. Includes states, DC, and US territories.
    private static readonly Dictionary<string, string> NameToCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALABAMA"]                        = "AL",
        ["ALASKA"]                         = "AK",
        ["ARIZONA"]                        = "AZ",
        ["ARKANSAS"]                       = "AR",
        ["CALIFORNIA"]                     = "CA",
        ["COLORADO"]                       = "CO",
        ["CONNECTICUT"]                    = "CT",
        ["DELAWARE"]                       = "DE",
        ["DISTRICT OF COLUMBIA"]           = "DC",
        ["FLORIDA"]                        = "FL",
        ["GEORGIA"]                        = "GA",
        ["HAWAII"]                         = "HI",
        ["IDAHO"]                          = "ID",
        ["ILLINOIS"]                       = "IL",
        ["INDIANA"]                        = "IN",
        ["IOWA"]                           = "IA",
        ["KANSAS"]                         = "KS",
        ["KENTUCKY"]                       = "KY",
        ["LOUISIANA"]                      = "LA",
        ["MAINE"]                          = "ME",
        ["MARYLAND"]                       = "MD",
        ["MASSACHUSETTS"]                  = "MA",
        ["MICHIGAN"]                       = "MI",
        ["MINNESOTA"]                      = "MN",
        ["MISSISSIPPI"]                    = "MS",
        ["MISSOURI"]                       = "MO",
        ["MONTANA"]                        = "MT",
        ["NEBRASKA"]                       = "NE",
        ["NEVADA"]                         = "NV",
        ["NEW HAMPSHIRE"]                  = "NH",
        ["NEW JERSEY"]                     = "NJ",
        ["NEW MEXICO"]                     = "NM",
        ["NEW YORK"]                       = "NY",
        ["NORTH CAROLINA"]                 = "NC",
        ["NORTH DAKOTA"]                   = "ND",
        ["OHIO"]                           = "OH",
        ["OKLAHOMA"]                       = "OK",
        ["OREGON"]                         = "OR",
        ["PENNSYLVANIA"]                   = "PA",
        ["RHODE ISLAND"]                   = "RI",
        ["SOUTH CAROLINA"]                 = "SC",
        ["SOUTH DAKOTA"]                   = "SD",
        ["TENNESSEE"]                      = "TN",
        ["TEXAS"]                          = "TX",
        ["UTAH"]                           = "UT",
        ["VERMONT"]                        = "VT",
        ["VIRGINIA"]                       = "VA",
        ["WASHINGTON"]                     = "WA",
        ["WEST VIRGINIA"]                  = "WV",
        ["WISCONSIN"]                      = "WI",
        ["WYOMING"]                        = "WY",
        // Territories and associated areas
        ["AMERICAN SAMOA"]                 = "AS",
        ["GUAM"]                           = "GU",
        ["NORTHERN MARIANA ISLANDS"]       = "MP",
        ["PUERTO RICO"]                    = "PR",
        ["U.S. VIRGIN ISLANDS"]            = "VI",
        ["US VIRGIN ISLANDS"]              = "VI",
        ["VIRGIN ISLANDS"]                 = "VI",
        // Canadian provinces (in case any accounts use them)
        ["ALBERTA"]                        = "AB",
        ["BRITISH COLUMBIA"]               = "BC",
        ["MANITOBA"]                       = "MB",
        ["NEW BRUNSWICK"]                  = "NB",
        ["NEWFOUNDLAND AND LABRADOR"]      = "NL",
        ["NORTHWEST TERRITORIES"]          = "NT",
        ["NOVA SCOTIA"]                    = "NS",
        ["NUNAVUT"]                        = "NU",
        ["ONTARIO"]                        = "ON",
        ["PRINCE EDWARD ISLAND"]           = "PE",
        ["QUEBEC"]                         = "QC",
        ["SASKATCHEWAN"]                   = "SK",
        ["YUKON"]                          = "YT",
    };

    // Valid 2-letter codes, so we can recognize an already-abbreviated input.
    private static readonly HashSet<string> ValidCodes =
        new(NameToCode.Values, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Converts a Salesforce BillingState value to its 2-letter postal code.
    /// - Full names ("Texas") are converted to the code ("TX").
    /// - Values already 2 letters ("TX", or "tx") are upper-cased and returned as-is if valid.
    /// - Null, empty, or unrecognized values return an empty string, which the
    ///   sp_SF_AddCashCust procedure treats as "no state provided".
    /// </summary>
    public static string ToStateCode(string? billingState)
    {
        if (string.IsNullOrWhiteSpace(billingState))
            return "";

        var trimmed = billingState.Trim();

        // Already a valid 2-letter code?
        if (trimmed.Length == 2 && ValidCodes.Contains(trimmed))
            return trimmed.ToUpperInvariant();

        // Full name → code
        if (NameToCode.TryGetValue(trimmed, out var code))
            return code;

        // Unrecognized — return empty rather than passing bad data to a varchar(2).
        // (An unrecognized 3+ char value would otherwise be silently truncated.)
        return "";
    }
}
