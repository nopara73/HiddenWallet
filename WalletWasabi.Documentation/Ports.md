# Ports

A reference of common local ports used by Wasabi and related software.
HiddenWallet's ports (3712x) are chosen within a long range of unassigned IANA ports, based on [this](https://stackoverflow.com/a/28369841/2061103) statistic, but also checked against [Service Name and Transport Protocol Port Number Registry](https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.xhtml).

|Port  | Application |
|----  | ---- |
|37120 | HiddenWallet API |
|37121 | Tor socks port used by HiddenWallet |
|37122 | Tor control port used by HiddenWallet |
|37123 | NTumbleBit server |
|37124 | Tor socks port used by NTumbleBit |
|37125 | Tor control port used by NTumbleBit |
|37126 | Chaumian Tumbler |
|37127 | Wallet Wasabi Backend |
|37128 | Wallet Wasabi RPC Server |
|37129 | Wallet Wasabi Local Client TCPListener on MainNet |
|37130 | Wallet Wasabi Local Client TCPListener on TestNet |
|37131 | Wallet Wasabi Local Client TCPListener on Others |
|9050  | Default Tor socks port |
|9051  | Default Tor control port |
|9150  | Tor socks port used by Tor Browser |
|9151  | Tor control port used by Tor Browser |
|8333  | Bitcoin Core Mainnet RPC |
|18333  | Bitcoin Core Testnet RPC |
|18444  | Bitcoin Core Regtest RPC |
|5000  | Stratis: Bitcoin node and Breeze Wallet API |
|5105  | Stratis: Stratis node and Stratis Wallet API |
