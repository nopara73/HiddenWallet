using System.Collections.ObjectModel;
using System.Linq;

namespace WalletWasabi.Fluent.Morph
{
	public class PolyLine
	{
		public ObservableCollection<double> XValues { get; set; }
		public ObservableCollection<double> YValues { get; set; }

		public PolyLine Clone()
		{
			return new()
			{
				XValues = new ObservableCollection<double>(XValues),
				YValues = new ObservableCollection<double>(YValues)
			};
		}

		public void Normalize()
		{
			var xMin = XValues.Min();
			var xMax = XValues.Max();
			var yMin = YValues.Min();
			var yMax = YValues.Max();

			for (var i = 0; i < XValues.Count; i++)
			{
				var x = XValues[i];
				XValues[i] = (x - xMin) / (xMax - xMin);
			}

			for (var i = 0; i < YValues.Count; i++)
			{
				var y = YValues[i];
				YValues[i] = (y - yMin) / (yMax - yMin);
			}
		}
	}
}