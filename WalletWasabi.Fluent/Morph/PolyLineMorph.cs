using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Animation.Easings;
using WalletWasabi.Fluent.MathNet;

namespace WalletWasabi.Fluent.Morph
{
	public static class PolyLineMorph
	{
		public static List<PolyLine> ToCache(PolyLine source, PolyLine target, double speed, IEasing easing)
		{
			int steps = (int) (1 / speed);
			double p = speed;
			var cache = new List<PolyLine>(steps);

			for (int i = 0; i < steps; i++)
			{
				var clone = source.Clone();
				var easeP = easing.Ease(p);

				To(clone, target, easeP);

				p += speed;

				cache.Add(clone);
			}

			return cache;
		}

		public static void To(PolyLine source, PolyLine target, double progress)
		{
			if (source.XValues.Count < target.XValues.Count)
			{
				InterpolatePolyLine(
					source.XValues.ToArray(),
					source.YValues.ToArray(),
					target.XValues.Count,
					out var xValues,
					out var yValues);
				source.XValues = xValues;
				source.YValues = yValues;
			}
			else if (source.XValues.Count > target.XValues.Count)
			{
				InterpolatePolyLine(
					target.XValues.ToArray(),
					target.YValues.ToArray(),
					source.XValues.Count,
					out var xValues,
					out var yValues);
				target.XValues = xValues;
				target.YValues = yValues;
			}

			for (int j = 0; j < source.XValues.Count; j++)
			{
				// TODO: For smooth animation we set all XValues on each frame to target without interpolation.
				// TODO: source.XValues[j] = Interpolate(source.XValues[j], target.XValues[j], progress);
				source.XValues[j] = target.XValues[j];
				source.YValues[j] = Interpolate(source.YValues[j], target.YValues[j], progress);
			}
		}

		public static double Interpolate(double from, double to, double progress)
		{
			return from + (to - from) * progress;
		}

		public static void InterpolatePolyLine(double[] xs, double[] ys, int count, out ObservableCollection<double> xValues, out ObservableCollection<double> yValues)
		{
			var a = xs.Min();
			var b = xs.Max();
			var range = b - a;
			var step = range / count;
			var spline = CubicSpline.InterpolatePchipSorted(xs, ys);

			xValues = new ObservableCollection<double>();
			yValues = new ObservableCollection<double>();

			for (var x = a + step; x < b; x += step)
			{
				var y = spline.Interpolate(x);
				xValues.Add(x);
				yValues.Add(y);
			}
		}
	}
}