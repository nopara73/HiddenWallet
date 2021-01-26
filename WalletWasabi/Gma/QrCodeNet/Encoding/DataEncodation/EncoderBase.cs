namespace Gma.QrCodeNet.Encoding.DataEncodation
{
	public abstract class EncoderBase
	{
		internal EncoderBase()
		{
		}

		/// <summary>
		/// Returns the bit representation of input data.
		/// </summary>
		internal abstract BitList GetDataBits(string content);

		/// <summary>
		/// Returns bit representation of Modevalue.
		/// </summary>
		/// <remarks>See Chapter 8.4 Data encodation, Table 2 — Mode indicators</remarks>
		internal BitList GetModeIndicator()
		{
			BitList modeIndicatorBits = new BitList
			{
				{ 0001 << 2, 4 }
			};
			return modeIndicatorBits;
		}

		internal BitList GetCharCountIndicator(int characterCount, int version)
		{
			BitList characterCountBits = new BitList();
			int bitCount = GetBitCountInCharCountIndicator(version);
			characterCountBits.Add(characterCount, bitCount);
			return characterCountBits;
		}

		/// <summary>
		/// Defines the length of the Character Count Indicator,
		/// which varies according to the mode and the symbol version in use
		/// </summary>
		/// <returns>Number of bits in Character Count Indicator.</returns>
		/// <remarks>
		/// See Chapter 8.4 Data encodation, Table 3 — Number of bits in Character Count Indicator.
		/// </remarks>
		protected abstract int GetBitCountInCharCountIndicator(int version);
	}
}
