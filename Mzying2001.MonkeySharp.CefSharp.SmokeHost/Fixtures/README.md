# Loopback fixtures

The `.user.js` files run only against the local compatibility server.

`loopback.pfx` is a public **test-only** self-signed keypair, password `fixture`. It has no production identity or credentials and is embedded solely for the TLS Cookie gate. SmokeHost enables certificate-error ignoring and maps `cookie.monkeysharp.test` to 127.0.0.1 in its own CEF process. The production adapter and Demo do not apply either setting. No certificate is installed into a system trust store. A synthetic test hostname avoids the version-dependent Secure Cookie exception for HTTP loopback hosts.

To regenerate with OpenSSL from the repository root:

```powershell
openssl req -x509 -newkey rsa:2048 -keyout artifacts/loopback-key.pem -out artifacts/loopback-cert.pem -days 3650 -nodes -subj /CN=localhost -addext 'subjectAltName=IP:127.0.0.1,DNS:localhost'
openssl pkcs12 -export -legacy -out Mzying2001.MonkeySharp.CefSharp.SmokeHost/Fixtures/loopback.pfx -inkey artifacts/loopback-key.pem -in artifacts/loopback-cert.pem -passout pass:fixture
```
