using System.Text.RegularExpressions;

namespace SmartParking.Application.Common.Helpers;

public static class PlateNumberHelper
{
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex VietnameseCarPlateRegex = new(@"^\d{2}[A-Z]\d{5}$", RegexOptions.Compiled);

    /// <summary>
    /// Chuẩn hóa biển số xe: loại bỏ khoảng trắng dư thừa và chuyển sang chữ hoa (in hoa).
    /// </summary>
    public static string Normalize(string? plateNumber)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
        {
            return string.Empty;
        }

        var trimmed = plateNumber.Trim().ToUpperInvariant();
        return MultipleSpacesRegex.Replace(trimmed, " ");
    }

    /// <summary>
    /// Kiểm tra biển số ô tô dân sự Việt Nam: mã tỉnh 2 số, series 1 chữ cái, số thứ tự 5 số.
    /// Dấu cách, gạch nối và dấu chấm được bỏ qua khi kiểm tra (ví dụ 51F-123.45).
    /// </summary>
    public static bool IsValidPlateFormat(string? plateNumber)
    {
        return VietnameseCarPlateRegex.IsMatch(NormalizeForComparison(plateNumber));
    }

    /// <summary>
    /// Chuẩn hóa biển số để đối chiếu (loại bỏ ký tự phân cách như dấu cách, gạch nối, chấm và in hoa).
    /// </summary>
    public static string NormalizeForComparison(string? plateNumber)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(plateNumber.Length);
        foreach (char c in plateNumber)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Kiểm tra hai chuỗi biển số có trùng khớp danh tính hay không.
    /// </summary>
    public static bool IsMatch(string? plate1, string? plate2)
    {
        var norm1 = NormalizeForComparison(plate1);
        var norm2 = NormalizeForComparison(plate2);
        return !string.IsNullOrEmpty(norm1) && norm1 == norm2;
    }
}
