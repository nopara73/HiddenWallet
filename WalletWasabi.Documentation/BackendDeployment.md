# Update

Consider updating the versions in `WalletWasabi.Helpers.Constants`. If the versions are updated, make sure the Client Release is already available before updating the backend.

```sh
sudo apt-get update && cd ~/WalletWasabi && git pull && cd ~/WalletWasabi/WalletWasabi.Backend && dotnet restore && cd ~
sudo systemctl stop walletwasabi.service
bitcoin-cli stop
sudo apt-get upgrade -y && sudo apt-get autoremove -y
# in case a new kernel is available:
sudo reboot
# service is restarted after boot, so stop it again
sudo systemctl stop walletwasabi.service

set DOTNET_CLI_TELEMETRY_OPTOUT=1
bitcoind
bitcoin-cli getblockchaininfo
dotnet publish ~/WalletWasabi/WalletWasabi.Backend --configuration Release --self-contained false
sudo systemctl start walletwasabi.service
pgrep -ilfa bitcoin
systemctl status tor@default
systemctl status walletwasabi
systemctl status nginx
tail -10000 ~/.walletwasabi/backend/Logs.txt
```

# 1. Create Remote Server

## Name
WalletWasabi.Backend.[TestNet/Main]

## Image
Ubuntu 18.04 x64

## Region
Mostly anywhere is fine, except the US or China.

## Size

https://bitcoin.org/en/full-node#minimum-requirements

[4GB Standard/32GB Standard]

# 2. Setup Server

https://www.digitalocean.com/community/tutorials/initial-server-setup-with-ubuntu-18-04

## SSH in as Root

Putty (Copypaste with Ctrl+Insert and Shift+Insert)
https://www.digitalocean.com/community/tutorials/how-to-use-ssh-keys-with-putty-on-digitalocean-droplets-windows-users

### Create a New User and Grant Administrative Privileges

```sh
adduser user
usermod -aG sudo user
```

# Setup Firewall

https://www.digitalocean.com/community/tutorials/how-to-set-up-a-firewall-with-ufw-on-ubuntu-14-04

```sh
ufw allow OpenSSH
ufw enable
```

> As the firewall is currently blocking all connections except for SSH, if you install and configure additional services, you will need to adjust the firewall settings to allow acceptable traffic in. You can learn some common UFW operations in this guide.
> https://www.digitalocean.com/community/tutorials/ufw-essentials-common-firewall-rules-and-commands

## Enable External Access for User

```sh
rsync --archive --chown=user:user ~/.ssh /home/user
```

## Update Ubuntu

```sh
sudo apt-get update && sudo apt-get dist-upgrade -y
```

# 3. Install .NET SDK

https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu

Opt out of the telemetry:

```sh
pico /home/user/.bashrc
```

Add:

```sh
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

and apply it to the current session as well:

```sh
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

# 4. Install Tor

```sh
sudo apt-get install tor
```

Check if Tor is running in the background:

```sh
systemctl status tor@default
```

Edit torrc (but beware updates) or read below:

```sh
sudo pico /etc/tor/torrc
```

```sh
Log notice file /home/user/.walletwasabi/notices.log

HiddenServiceDir /home/user/.hidden_service_v3
HiddenServiceVersion 3
HiddenServicePort 80 127.0.0.1:37127

RunAsDaemon 1

# ---MAKE TOR FASTER---

# Need to disable for HiddenServiceNonAnonymousMode
SOCKSPort 0
# Need to enable for HiddenServiceSingleHopMode
HiddenServiceNonAnonymousMode 1
# This option makes every hidden service instance hosted by a tor
# instance a Single Onion Service. One-hop circuits make Single Onion
# servers easily locatable, but clients remain location-anonymous.
HiddenServiceSingleHopMode 1
```

However there's annoyance and a risk you'll rewrite it accidentally on upgrade.
If you want to avoid this risk
[Cryptoanarchy Debian Repository](https://github.com/Kixunil/cryptoanarchy-deb-repo-builder/)
contains a simple package that takes care of it. After adding the repository:

```sh
sudo apt install tor-hs-patch-config
```

Then place:

```
HiddenServiceDir /home/user/.hidden_service_v3
HiddenServiceVersion 3
HiddenServicePort 80 127.0.0.1:37127
```

into `/etc/tor/hidden_services.d/wasabi.conf`

and

```
Log notice file /home/user/.walletwasabi/notices.log

# ---MAKE TOR FASTER---

# Need to disable for HiddenServiceNonAnonymousMode
SOCKSPort 0
# Need to enable for HiddenServiceSingleHopMode
HiddenServiceNonAnonymousMode 1
# This option makes every hidden service instance hosted by a tor
# instance a Single Onion Service. One-hop circuits make Single Onion
# servers easily locatable, but clients remain location-anonymous.
HiddenServiceSingleHopMode 1
```

into `/etc/tor/conf.d/wasabi.conf`


Enable firewall:
```sh
sudo ufw allow 80
```

**Backup the generated private key!**

The key is located in `/home/user/.hidden_service_v3`

## Update Tor

Tor package provided by the OS should have all security fixes and the maintainers of the OS
are responsible for making sure it works. However if you must have the latest version:

```
$ sudo pico /etc/apt/sources.list

Append these two lines:
deb https://deb.torproject.org/torproject.org bionic main
deb-src https://deb.torproject.org/torproject.org bionic main

$ curl https://deb.torproject.org/torproject.org/A3C4F0F979CAA22CDBA8F512EE8CBC9E886DDD89.asc | gpg --import
$ gpg --export A3C4F0F979CAA22CDBA8F512EE8CBC9E886DDD89 | sudo apt-key add -
$ sudo apt update
$ sudo apt install tor
IMPORTANT! Press N otherwise it will replace the torrc with a default version and bye wasabi hidden service
```

# 5. Install, Configure and Synchronize bitcoind (Bitcoin Knots)

https://bitcoinknots.org/

```sh
sudo add-apt-repository ppa:luke-jr/bitcoinknots
sudo apt-get update
sudo apt-get install bitcoind
mkdir ~/.bitcoin
pico ~/.bitcoin/bitcoin.conf
```

```sh
testnet=[0/1]

[main/test].rpcworkqueue=256

[main/test].txindex=1

[main/test].daemon=1
[main/test].server=1
[main/test].rpcuser=bitcoinuser
[main/test].rpcpassword=password
[main/test].whitebind=127.0.0.1:[8333/18333]
[main/test].mempoolreplacement=fee,optin # Only valid for Bitcoin Knots - https://github.com/MetacoSA/NBitcoin/pull/884#issuecomment-663620290
#[main/test].debug=rpc     # in some cases it could be good to uncomment this line.
```
https://bitcoincore.org/en/releases/0.17.0/
https://medium.com/@loopring/how-to-run-lighting-btc-node-and-start-mining-b55c4bab8ad
https://github.com/MrChrisJ/fullnode/issues/18

```sh
sudo ufw allow ssh
sudo ufw allow [18333/8333]
bitcoind
bitcoin-cli getblockcount
bitcoin-cli stop
bitcoind
```

# 6. Publish, Configure and Run WalletWasabi.Backend

```sh
git clone https://github.com/zkSNACKs/WalletWasabi.git
cd WalletWasabi
dotnet restore
dotnet build
dotnet publish WalletWasabi.Backend --configuration Release --self-contained false
dotnet WalletWasabi.Backend/bin/Release/net5.0/publish/WalletWasabi.Backend.dll
cd ..
cat .walletwasabi/backend/Logs.txt
pico .walletwasabi/backend/Config.json
pico .walletwasabi/backend/CcjRoundConfig.json
dotnet WalletWasabi/WalletWasabi.Backend/bin/Release/net5.0/publish/WalletWasabi.Backend.dll
cat .walletwasabi/backend/Logs.txt
```

# 7. Monitor the Apps

## WalletWasabi.Backend

https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-2.0&tabs=aspnetcore2x

```sh
sudo pico /etc/systemd/system/walletwasabi.service
```

```sh
[Unit]
Description=WalletWasabi Backend API

[Service]
WorkingDirectory=/home/user/WalletWasabi/WalletWasabi.Backend/bin/Release/net5.0/publish
ExecStart=/usr/bin/dotnet /home/user/WalletWasabi/WalletWasabi.Backend/bin/Release/net5.0/publish/WalletWasabi.Backend.dll
Restart=always
RestartSec=10  # Restart service after 10 seconds if dotnet service crashes
SyslogIdentifier=walletwasabi-backend
User=user
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
LimitNOFILE=16384

[Install]
WantedBy=multi-user.target
```

```sh
sudo systemctl daemon-reload
sudo systemctl enable walletwasabi.service
sudo systemctl start walletwasabi.service
systemctl status walletwasabi.service
tail -10000 .walletwasabi/backend/Logs.txt
```

## Tor

Tor should run automatically on boot. If it doesn't:

```sh
sudo systemctl start tor@default
```

Review the tor activity using the logs stored in the linux journal:

```sh
sudo journalctl -u tor@default
```

## Load balance and server performance

Check load avarages
```sh
uptime
```
Check the number of CPU-s
```sh
nproc
```

Load average numbers are in order according to the average time-window in the last - 1, 5, 15 minutes. Zero means no load, 1 means 100% load - however, average loads are added up among the number of CPUs. So as far as the load average is not bigger than the number of CPUs, there shouldn't be any performance issues.

For interactive monitoring you can use:
```sh
htop
```

# 8. Setup Nginx

https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-2.0&tabs=aspnetcore2x#install-nginx
Only setup Nginx if you want to expose the autogenerated website to the clearnet.

Enable firewall:
```sh
sudo ufw allow http
sudo ufw allow https
```

```sh
sudo apt-get install nginx -y
sudo systemctl start nginx
```
Verify the browser displays the default landing page for Nginx.
The landing page is reachable at `http://<server_IP_address>/index.nginx-debian.html`

```sh
sudo pico /etc/nginx/sites-available/default
```

Fill out the first server's name with the server's IP and domain, and remove the unneeded domains and the second server. (Note that I use `wasabiwallet.co` for testnet.)

```
server {
    listen        80;
    listen        [::]:80;
    listen        443 ssl;
    listen        [::]:443 ssl;
    server_name   [InsertServerIPHere] wasabiwallet.net www.wasabiwallet.net wasabiwallet.org www.wasabiwallet.org wasabiwallet.info www.wasabiwallet.info wasabiwallet.co www.wasabiwallet.co zerolink.info www.zerolink.info hiddenwallet.org www.hiddenwallet.org;
    location / {
        sub_filter '<head>'  '<head><meta name="robots" content="noindex, nofollow" />';
        sub_filter_once on;
        proxy_pass         http://localhost:37127;
    }
}

server {
    listen        80;
    listen        [::]:80;
    listen        443 ssl;
    listen        [::]:443 ssl;
    server_name   wasabiwallet.io www.wasabiwallet.io;
    location / {
        proxy_pass         http://localhost:37127;
    }
}
```

```sh
# Test that nginx configuration is valid
sudo nginx -t
# Reload nginx
sudo systemctl reload nginx
```

Setup https, redirect to https when asks. This will modify the above config file, but oh well.

```sh
sudo apt install certbot
sudo certbot -d wasabiwallet.io -d www.wasabiwallet.io -d wasabiwallet.net -d www.wasabiwallet.net -d wasabiwallet.org -d www.wasabiwallet.org -d wasabiwallet.info -d www.wasabiwallet.info -d wasabiwallet.co -d www.wasabiwallet.co -d zerolink.info -d www.zerolink.info -d hiddenwallet.org -d www.hiddenwallet.org
```

certbot will not properly redirect www, so it must be setup by hand, one by one.
Duplicate all entries like this by adding a `www.`:
```
server {
    if ($host = wasabiwallet.co) {
        return 301 https://$host$request_uri;
    }
}
```

Add `add_header Strict-Transport-Security "max-age=31536000; includeSubDomains; preload" always;` and `server_tokens off;` to every HTTPS `server` block.

```sh
sudo nginx -t
sudo systemctl reload nginx
```

After accessing the website finalize preload in https://hstspreload.org/

# Check If Everything Works

TestNet: http://testwnp3fugjln6vh5vpj7mvq3lkqqwjj3c2aafyu7laxz42kgwh2rad.onion/swagger/
Main: http://wasabiukrxmkdgve5kynjztuovbg43uxcbcxn6y2okcrsg7gb6jdmbad.onion/swagger/
GET fees

http://www.wasabiwallet.io/

# Check Statuses

```sh
tail -f ~/.bitcoin/debug.log
tail -10000 .walletwasabi/backend/Logs.txt
du -bsh .walletwasabi/backend/IndexBuilderService/*
```
# Specify Your ExtPubKey

Take your ExtPubKey from Wasabi. Never receive money to that wallet's external keypath.

```sh
pico ~/.walletwasabi/backend/CcjRoundConfig.json
```

Add your extpub to the `CoordinatorExtPubKey`.

# Additional (optional) Settings

## Rolling Bitcoin Knots node debug logs

The following command line adds a configuration file to let logrotate service know
how to rotate the bitcoin debug logs.

```sh
sudo tee -a /etc/logrotate.d/bitcoin <<EOS
/home/user/.bitcoin/debug.log
{
        su user user
        rotate 5
        copytruncate
        daily
        missingok
        notifempty
        compress
        delaycompress
        sharedscripts
}
EOS
```

**Note:** In test server replace the first line by the following one `/home/user/.bitcoin/testnet3/debug.log`

## Welcome Banner

The following command line adds a welcome banner indicating the ssh logged user that he is in the production server.

```sh
sudo tee -a /etc/motd <<EOS
****************************************************************************
*** Attention! Wasabi PRODUCTION server                                  ***
****************************************************************************
EOS
```

## Prompt

Additionally to the welcome banner it could be good to know in which server we are all the time and to see clearly which branch is checked out, in this case update the prompt as follow:

```sh
pico ~/.bashrc
```

Replace the line:

```sh
PS1='${debian_chroot:+($debian_chroot)}\[\033[01;32m\]\u@\h\[\033[00m\]:\[\033[01;34m\]\w\[\033[00m\]\$ '
```

by this one:

```sh
PS1='${debian_chroot:+($debian_chroot)}\[\033[01;32m\]\u@\h\[\033[00m\]:(PROD):\[\033[01;34m\]\w\[\033[01;31m\]$(parse_git_branch)\[\033[00m\]\$ '
```

Additionally add the following function before:

```sh
parse_git_branch() {
 git branch 2> /dev/null | sed -e '/^[^*]/d' -e 's/* \(.*\)/(\1)/'
}
```

**Note:** In the test server replace the word **PROD** by **TEST**

## Let's Encrypt

[Let’s Encrypt](https://letsencrypt.org/about/) is a free, automated, and open certificate authority (CA), run for the public’s benefit.
It is renewed automatically by certbot which is an agent software installed on both backends. A newly created or renewed certificates are valid for 90 days and the renewal process should start automatically (`cronjob`) if the certificate will expire in less than 30 days.

You can list the certificates with:

`sudo certbot certificates`

Check all of the certificates that you’ve obtained and tries to renew any that will expire in less than 30 days (this should be automatic):

`sudo certbot renew`

Detailed instuctions about configuration [here](https://certbot.eff.org/lets-encrypt/ubuntubionic-nginx).
