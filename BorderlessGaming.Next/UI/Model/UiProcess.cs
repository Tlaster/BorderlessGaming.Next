using System;
using BorderlessGaming.Next.Common;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BorderlessGaming.Next.UI.Model;

internal record UiProcess(string Title, string SubTitle, BitmapSource? Icon, Native.ProcessData Data)
{
    public virtual bool Equals(UiProcess? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Data.Equals(other.Data) && Title == other.Title && SubTitle == other.SubTitle && Equals(Icon, other.Icon);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Data, Title, SubTitle, Icon);
    }

    public Native.ProcessData Data { get; set; } = Data;
    public string Title { get; set; } = Title;
    public string SubTitle { get; set; } = SubTitle;
    public BitmapSource? Icon { get; set; } = Icon;
}