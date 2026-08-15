using FolderSail.Core.Models;
using FolderSail.Mvvm;

namespace FolderSail.ViewModels;

public sealed class DriveItemViewModel : ObservableObject
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public DriveItemViewModel(DriveItem model)
    {
        Model = model;
    }

    public DriveItem Model { get; }

    public string Name => Model.Name;
    public string Path => Model.Path;
    public string Label => Model.Label;
    public double UsedRatio => Model.UsedRatio;

    public string CapacityText => $"{Format(Model.FreeSize)} 可用 / {Format(Model.TotalSize)}";

    public bool IsAlmostFull => Model.UsedRatio >= 0.9;

    private static string Format(long size)
    {
        if (size <= 0)
        {
            return "0 B";
        }

        var order = 0;
        double value = size;
        while (value >= 1024 && order < Units.Length - 1)
        {
            order++;
            value /= 1024;
        }

        return $"{value:0.#} {Units[order]}";
    }
}
