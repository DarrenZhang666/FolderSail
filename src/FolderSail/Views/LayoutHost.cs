using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using FolderSail.Core.Models;
using FolderSail.ViewModels;

namespace FolderSail.Views;

public class LayoutHost : ItemsControl
{
    private INotifyCollectionChanged? _collectionSubscription;

    static LayoutHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LayoutHost), new FrameworkPropertyMetadata(typeof(LayoutHost)));
    }

    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.Register(nameof(LayoutMode), typeof(LayoutMode), typeof(LayoutHost),
            new PropertyMetadata(LayoutMode.Dual, OnLayoutChanged));

    public LayoutMode LayoutMode
    {
        get => (LayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateGrid();
    }

    protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);

        if (_collectionSubscription != null)
        {
            _collectionSubscription.CollectionChanged -= OnCollectionChanged;
            _collectionSubscription = null;
        }

        if (newValue is INotifyCollectionChanged collection)
        {
            _collectionSubscription = collection;
            _collectionSubscription.CollectionChanged += OnCollectionChanged;
        }

        UpdateGrid();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LayoutModeProperty)
        {
            UpdateGrid();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateGrid();

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LayoutHost host)
        {
            host.UpdateGrid();
        }
    }

    private void UpdateGrid()
    {
        if (Template?.FindName("PART_Grid", this) is not Grid grid)
        {
            return;
        }

        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        grid.Children.Clear();

        var definition = LayoutMode.GetDefinition();

        foreach (var weight in definition.RowWeights)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(weight, GridUnitType.Star),
                MinHeight = 90
            });
        }

        foreach (var weight in definition.ColumnWeights)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(weight, GridUnitType.Star),
                MinWidth = 150
            });
        }

        var index = 0;
        foreach (var item in Items)
        {
            if (index >= definition.Placements.Count)
            {
                break;
            }

            var placement = definition.Placements[index];

            var paneView = new PaneView
            {
                DataContext = item
            };

            Grid.SetRow(paneView, placement.Row);
            Grid.SetColumn(paneView, placement.Column);
            Grid.SetRowSpan(paneView, placement.RowSpan);
            Grid.SetColumnSpan(paneView, placement.ColumnSpan);
            grid.Children.Add(paneView);
            index++;
        }
    }
}
