using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BorderlessGaming.Next.Common;
using BorderlessGaming.Next.UI.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.UI;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BorderlessGaming.Next.UI.Scene.Home;

[ObservableObject]
internal partial class ProcessViewModel
{
    private Task _refreshTask;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    private UiProcess? _selectedProcess = null;
    public bool CanExecute => SelectedProcess != null;
    [ObservableProperty]
    private string _filter = string.Empty;
    partial void OnFilterChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            CollectionView.Filter = null;
        }
        else
        {
            CollectionView.Filter = item =>
            {
                if (item is not UiProcess process)
                {
                    return false;
                }

                return process.Title.Contains(value, StringComparison.OrdinalIgnoreCase) || process.SubTitle.Contains(value, StringComparison.OrdinalIgnoreCase);
            };
        }
    }

    public ObservableCollection<UiProcess> Processes { get; }
    public AdvancedCollectionView CollectionView { get; }
    public ProcessViewModel()
    {
        Processes = new ObservableCollection<UiProcess>();
        CollectionView = new AdvancedCollectionView(Processes, true)
        {
            SortDescriptions =
            {
                new SortDescription(nameof(UiProcess.Title), SortDirection.Ascending)
            }
        };
        _refreshTask = Task.Run((Func<Task?>)(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                RefreshCommand.Execute(null);
            }
        }));
        RefreshCommand.Execute(null);
    }
    
    [RelayCommand]
    private async Task Refresh()
    {
        var result = await Native.QueryProcessesWithWindows();
        foreach (var data in result)
        {
            if (Processes.Any(it => it.Data.Handle == data.Handle))
            {
                continue;
            }
            var title = Native.GetWindowTitle(data.Handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            BitmapSource? icon;
            try
            {
                icon = await Native.GetWindowIcon(data.Handle);
            }
            catch
            {
                icon = null;
            }
            Processes.Add(new UiProcess(title, data.Process.ProcessName, icon, data));
        }
        var processToRemove = Processes.Where(it => result.All(data => data.Handle != it.Data.Handle)).ToList();
        foreach (var process in processToRemove)
        {
            Processes.Remove(process);
        }
    }

    [RelayCommand]
    private async Task Borderless()
    {
        if (SelectedProcess == null)
        {
            return;
        }
        await Native.SetWindowBorderless(SelectedProcess.Data);
    }
    
    [RelayCommand]
    private void Windowed()
    {
        if (SelectedProcess == null)
        {
            return;
        }
        Native.RestoreWindow(SelectedProcess.Data);
    }
}