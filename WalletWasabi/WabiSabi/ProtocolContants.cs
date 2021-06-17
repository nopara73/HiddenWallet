namespace WalletWasabi.WabiSabi
{
	public static class ProtocolConstants
	{
		public const int CredentialNumber = 2;
		public const long MaxAmountPerAlice = 4_300_000_000_000L;
		public const long MaxVsizeCredentialValue = 255;

		public const string WabiSabiProtocolIdentifier = "WabiSabi_v1.0";
		public const string DomainStrobeSeparator = "domain-separator";

		// Round hashing labels
		public const string RoundStrobeDomain = "round-parameters";
		public const string RoundMinRegistrableAmountStrobeLabel = "minimum-registrable-amount";
		public const string RoundMaxRegistrableAmountStrobeLabel = "maximum-registrable-amount";
		public const string RoundMaxRegistrableVsizeStrobeLabel = "maximum-registrable-vsize";
		public const string RoundMaxVsizePerAliceStrobeLabel = "per-alice-vsize-allocation";
		public const string RoundAmountCredentialIssuerParametersStrobeLabel = "amount-credential-issuer-parameters";
		public const string RoundVsizeCredentialIssuerParametersStrobeLabel = "vsize-credential-issuer-parameters";
		public const string RoundFeeRateStrobeLabel = "fee-rate";

		public const string RoundInputRegistrationTimeoutLabel= "input-registration-timeout";
		public const string RoundConnectionConfirmationTimeoutLabel= "connection-confirmation-timeout";
		public const string RoundOutputRegistrationTimeoutLabel= "output-registration-timeout";
		public const string RoundTransactionSigningTimeoutLabel= "transaction-signing-timeout";

		// Alice hashing labels
		public const string AliceStrobeDomain = "alice-parameters";
		public const string AliceCoinTxOutStrobeLabel = "coin-txout";
		public const string AliceCoinOutpointStrobeLabel = "coin-outpoint";
		public const string AliceOwnershipProofStrobeLabel = "ownership-proof";
		public const string ProtocolViolationType = "wabisabi-protocol-violation";
	}
}
