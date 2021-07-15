using System;
using NBitcoin;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.StrobeProtocol;

namespace WalletWasabi.WabiSabi.Crypto
{
	public static class RoundHasher
	{
		public static uint256 CalculateHash(
			TimeSpan inputRegistrationTimeout,
			TimeSpan connectionConfirmationTimeout,
			TimeSpan outputRegistrationTimeout,
			TimeSpan transactionSigningTimeout,
			long maxRegistrableAmount,
			long maxRegistrableVsize,
			long maxVsizeAllocationPerAlice,
			CredentialIssuerParameters amountCredentialIssuerParameters,
			CredentialIssuerParameters vsizeCredentialIssuerParameters,
			long feePerK)
			=> StrobeHasher.Create(ProtocolConstants.RoundStrobeDomain)
				.Append(ProtocolConstants.RoundInputRegistrationTimeoutLabel, inputRegistrationTimeout)
				.Append(ProtocolConstants.RoundConnectionConfirmationTimeoutLabel, connectionConfirmationTimeout)
				.Append(ProtocolConstants.RoundOutputRegistrationTimeoutLabel, outputRegistrationTimeout)
				.Append(ProtocolConstants.RoundTransactionSigningTimeoutLabel, transactionSigningTimeout)
				.Append(ProtocolConstants.RoundMaxRegistrableAmountStrobeLabel, maxRegistrableAmount)
				.Append(ProtocolConstants.RoundMaxRegistrableVsizeStrobeLabel, maxRegistrableVsize)
				.Append(ProtocolConstants.RoundMaxVsizePerAliceStrobeLabel, maxVsizeAllocationPerAlice)
				.Append(ProtocolConstants.RoundAmountCredentialIssuerParametersStrobeLabel, amountCredentialIssuerParameters)
				.Append(ProtocolConstants.RoundVsizeCredentialIssuerParametersStrobeLabel, vsizeCredentialIssuerParameters)
				.Append(ProtocolConstants.RoundFeeRateStrobeLabel, feePerK)
				.GetHash();
	}
}
