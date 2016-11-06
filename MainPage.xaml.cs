using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace AskChef
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>

	public class TextMessage
	{
		private static readonly SolidColorBrush _sentBackground = new SolidColorBrush(Color.FromArgb(255, 170, 20, 40));
		private static readonly SolidColorBrush _receivedBackground = new SolidColorBrush(Colors.Crimson);

		public string Message { get; set; }
		public DateTime Time { get; set; }
		public bool IsSent { get; set; }
		public SolidColorBrush Background { get { return IsSent ? _sentBackground : _receivedBackground; } }
		public Visibility SentVisibility { get { return IsSent ? Visibility.Visible : Visibility.Collapsed; } }
		public Visibility ReceiveVisibility { get { return !IsSent ? Visibility.Visible : Visibility.Collapsed; } }
	}

	public class Conversation : ObservableCollection<TextMessage>, ISupportIncrementalLoading
	{
		private uint messageCount = 0;

		public Conversation()
		{
		}

		public bool HasMoreItems { get; } = true;

		public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
		{
			this.CreateMessages(count);

			return Task.FromResult<LoadMoreItemsResult>(
				new LoadMoreItemsResult()
				{
					Count = count
				}).AsAsyncOperation();
		}

		private void CreateMessages(uint count)
		{
			for (uint i = 0; i < count; i++)
			{
				this.Insert(0, new TextMessage()
				{
					Message = $"{messageCount}: {CreateRandomMessage()}",
					IsSent = 0 == messageCount++ % 2,
					Time = DateTime.Now
				});
			}
		}

		private static Random rand = new Random();
		private static string fillerText = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";

		public static string CreateRandomMessage()
		{
			return fillerText.Substring(0, rand.Next(5, fillerText.Length));
		}
	}

	public class ChatListView : ListView
	{
		private uint itemsSeen;
		private double averageContainerHeight;
		private bool loadingInProcess = false;

		public ChatListView()
		{
			// We'll manually trigger the loading of data incrementally and buffer for 2 pages worth of data
			this.IncrementalLoadingTrigger = IncrementalLoadingTrigger.None;

			// Since we'll have variable sized items we compute a running average of height to help estimate
			// how much data to request for incremental loading
			this.ContainerContentChanging += this.UpdateRunningAverageContainerHeight;
		}

		protected override void OnApplyTemplate()
		{
			var scrollViewer = this.GetTemplateChild("ScrollViewer") as ScrollViewer;

			if (scrollViewer != null)
			{
				scrollViewer.ViewChanged += (s, a) =>
				{
					// Check if we should load more data when the scroll position changes.
					// We only get this once the content/panel is large enough to be scrollable.
					this.ProcessDataVirtualizationScrollOffsetsAsync(this.ActualHeight);
				};
			}

			base.OnApplyTemplate();
		}

		// We use ArrangeOverride to trigger incrementally loading data (if needed) when the panel is too small to be scrollable.
		protected override Size ArrangeOverride(Size finalSize)
		{
			// Allow the panel to arrange first
			var result = base.ArrangeOverride(finalSize);

			ProcessDataVirtualizationScrollOffsetsAsync(finalSize.Height);

			return result;
		}

		private async void ProcessDataVirtualizationScrollOffsetsAsync(double actualHeight)
		{
			var panel = this.ItemsPanelRoot as ItemsStackPanel;
			if (panel != null && !this.loadingInProcess)
			{
				if ((panel.FirstVisibleIndex != -1 && panel.FirstVisibleIndex * this.averageContainerHeight < actualHeight * this.IncrementalLoadingThreshold) ||
					(panel.FirstVisibleIndex == -1 && Items.Count <= 1))
				{
					var virtualizingDataSource = this.ItemsSource as ISupportIncrementalLoading;
					if (virtualizingDataSource != null)
					{
						if (virtualizingDataSource.HasMoreItems)
						{
							double avgItemsPerPage = actualHeight / this.averageContainerHeight;
							var itemsToLoad = (uint)(this.DataFetchSize * avgItemsPerPage);
							if (itemsToLoad <= 0)
							{
								// We know there's data to be loaded so load at least one item
								itemsToLoad = 1;
							}

							this.loadingInProcess = true;
							await virtualizingDataSource.LoadMoreItemsAsync(itemsToLoad);
							this.loadingInProcess = false;
						}
					}
				}
			}
		}

		private void UpdateRunningAverageContainerHeight(ListViewBase sender, ContainerContentChangingEventArgs args)
		{
			if (args.ItemContainer != null && args.InRecycleQueue != true)
			{
				if (args.Phase == 0)
				{
					// use the size of the very first placeholder as a starting point until
					// we've seen the first item
					if (this.averageContainerHeight == 0)
					{
						this.averageContainerHeight = args.ItemContainer.DesiredSize.Height;
					}

					args.RegisterUpdateCallback(1, this.UpdateRunningAverageContainerHeight);
					args.Handled = true;
				}
				else if (args.Phase == 1)
				{
					// set the content
					args.ItemContainer.Content = args.Item;
					args.RegisterUpdateCallback(2, this.UpdateRunningAverageContainerHeight);
					args.Handled = true;
				}
				else if (args.Phase == 2)
				{
					// refine the estimate based on the item's DesiredSize
					this.averageContainerHeight = (this.averageContainerHeight * itemsSeen + args.ItemContainer.DesiredSize.Height) / ++itemsSeen;
					args.Handled = true;
				}
			}
		}
	}

	public sealed partial class MainPage : Page
	{
		private readonly Conversation conversation = new Conversation();

		public MainPage()
		{
			this.InitializeComponent();
			ChatCanvas.ItemsSource = this.conversation;
			ChatCanvas.ContainerContentChanging += OnChatViewContainerContentChanging;
		}

		private void HamburgerButton_Click(object sender, RoutedEventArgs e)
		{
			NavigationMenu.IsPaneOpen = !NavigationMenu.IsPaneOpen;
		}

		async void SendTextMessage()
		{
			if (UserInputTextBox.Text.Length > 0)
			{
				this.conversation.Add(new TextMessage
				{
					Message = UserInputTextBox.Text,
					Time = DateTime.Now,
					IsSent = true
				});
				UserInputTextBox.Text = string.Empty;

				// Send a simulated reply after a brief delay.
				await Task.Delay(TimeSpan.FromSeconds(2));

				this.conversation.Add(new TextMessage
				{
					Message = Conversation.CreateRandomMessage(),
					Time = DateTime.Now,
					IsSent = false
				});
			}
		}

		private void UserInputTextBox_KeyUp(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Enter)
			{
				this.SendTextMessage();
			}
		}

		private void OnChatViewContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
		{
			if (args.InRecycleQueue) return;
			TextMessage message = (TextMessage)args.Item;
			args.ItemContainer.HorizontalAlignment = message.IsSent ? Windows.UI.Xaml.HorizontalAlignment.Right : Windows.UI.Xaml.HorizontalAlignment.Left;
		}
	}
}
