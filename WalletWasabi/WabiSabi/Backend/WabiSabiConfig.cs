using NBitcoin;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using WalletWasabi.Bases;
using WalletWasabi.JsonConverters.Bitcoin;
using WalletWasabi.JsonConverters.Timing;
using WalletWasabi.WabiSabi.Backend.Rounds;

namespace WalletWasabi.WabiSabi.Backend
{
	[JsonObject(MemberSerialization.OptIn)]
	public class WabiSabiConfig : ConfigBase
	{
		public WabiSabiConfig() : base()
		{
		}

		public WabiSabiConfig(string filePath) : base(filePath)
		{
		}

		[DefaultValue(108)]
		[JsonProperty(PropertyName = "ConfirmationTarget", DefaultValueHandling = DefaultValueHandling.Populate)]
		public uint ConfirmationTarget { get; set; } = 108;

		[DefaultValueTimeSpan("0d 3h 0m 0s")]
		[JsonProperty(PropertyName = "ReleaseUtxoFromPrisonAfter", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(TimeSpanJsonConverter))]
		public TimeSpan ReleaseUtxoFromPrisonAfter { get; set; } = TimeSpan.FromHours(3);

		[DefaultValueMoneyBtc("0.00005")]
		[JsonProperty(PropertyName = "MinRegistrableAmount", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(MoneyBtcJsonConverter))]
		public Money MinRegistrableAmount { get; set; } = Money.Coins(0.00005m);

		/// <summary>
		/// The width of the rangeproofs are calculated from this, so don't choose stupid numbers.
		/// </summary>
		[DefaultValueMoneyBtc("43000")]
		[JsonProperty(PropertyName = "MaxRegistrableAmount", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(MoneyBtcJsonConverter))]
		public Money MaxRegistrableAmount { get; set; } = Money.Coins(43_000m);

		[DefaultValue(true)]
		[JsonProperty(PropertyName = "AllowNotedInputRegistration", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool AllowNotedInputRegistration { get; set; } = true;

		[DefaultValueTimeSpan("0d 1h 0m 0s")]
		[JsonProperty(PropertyName = "StandardInputRegistrationTimeout", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(TimeSpanJsonConverter))]
		public TimeSpan StandardInputRegistrationTimeout { get; set; } = TimeSpan.FromHours(1);

		[DefaultValueTimeSpan("0d 0h 3m 0s")]
		[JsonProperty(PropertyName = "BlameInputRegistrationTimeout", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(TimeSpanJsonConverter))]
		public TimeSpan BlameInputRegistrationTimeout { get; set; } = TimeSpan.FromMinutes(3);

		[DefaultValueTimeSpan("0d 0h 1m 0s")]
		[JsonProperty(PropertyName = "ConnectionConfirmationTimeout", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(TimeSpanJsonConverter))]
		public TimeSpan ConnectionConfirmationTimeout { get; set; } = TimeSpan.FromMinutes(1);

		[DefaultValueTimeSpan("0d 0h 1m 0s")]
		[JsonProperty(PropertyName = "OutputRegistrationTimeout", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(TimeSpanJsonConverter))]
		public TimeSpan OutputRegistrationTimeout { get; set; } = TimeSpan.FromMinutes(1);

		[DefaultValueTimeSpan("0d 0h 1m 0s")]
		[JsonProperty(PropertyName = "TransactionSigningTimeout", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(TimeSpanJsonConverter))]
		public TimeSpan TransactionSigningTimeout { get; set; } = TimeSpan.FromMinutes(1);

		[DefaultValueTimeSpan("0d 0h 5m 0s")]
		[JsonProperty(PropertyName = "RoundExpiryTimeout", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(TimeSpanJsonConverter))]
		public TimeSpan RoundExpiryTimeout { get; set; } = TimeSpan.FromMinutes(5);

		[DefaultValue(100)]
		[JsonProperty(PropertyName = "MaxInputCountByRound", DefaultValueHandling = DefaultValueHandling.Populate)]
		public int MaxInputCountByRound { get; set; } = 100;

		[DefaultValue(0.5)]
		[JsonProperty(PropertyName = "MinInputCountByRoundMultiplier", DefaultValueHandling = DefaultValueHandling.Populate)]
		public double MinInputCountByRoundMultiplier { get; set; } = 0.5;

		public int MinInputCountByRound => (int)(MaxInputCountByRound * MinInputCountByRoundMultiplier);

		/// <summary>
		/// If money comes to the blame script, then either an attacker lost money or there's a client bug.
		/// </summary>
		[DefaultValueScript("0 1251dec2e6a6694a789f0cca6c2a9cfb4c74fb4e")]
		[JsonProperty(PropertyName = "BlameScript", DefaultValueHandling = DefaultValueHandling.Populate)]
		[JsonConverter(typeof(ScriptJsonConverter))]
		public Script BlameScript { get; set; } = new Script("0 1251dec2e6a6694a789f0cca6c2a9cfb4c74fb4e");

		public TimeSpan GetInputRegistrationTimeout(Round round)
			=> round.IsBlameRound ? BlameInputRegistrationTimeout : StandardInputRegistrationTimeout;
	}
}
