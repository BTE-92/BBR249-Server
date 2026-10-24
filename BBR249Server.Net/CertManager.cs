using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BBR249Server.Core;

namespace BBR249Server.Net;

public static class CertManager
{
	private const string RootSubject = "CN=BigBangRacing Offline Root CA";

	private static string HostsMarker => Settings.LeafPfx + ".hosts";

	public static X509Certificate2 EnsureServerCertificate()
	{
		using X509Certificate2 root = EnsureRootCa();
		string text = string.Join(",", Settings.TargetHosts.OrderBy((string h) => h));
		if (File.Exists(Settings.LeafPfx) && File.Exists(HostsMarker) && File.ReadAllText(HostsMarker).Trim() == text)
		{
			try
			{
				X509Certificate2 x509Certificate = new X509Certificate2(Settings.LeafPfx, (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
				if (x509Certificate.NotAfter > DateTime.UtcNow.AddDays(30.0))
				{
					return x509Certificate;
				}
				x509Certificate.Dispose();
			}
			catch
			{
			}
		}
		X509Certificate2 x509Certificate2 = CreateLeaf(root);
		File.WriteAllBytes(Settings.LeafPfx, x509Certificate2.Export(X509ContentType.Pfx));
		File.WriteAllText(HostsMarker, text);
		Console.WriteLine("[Setup] Generated a new HTTPS certificate for: " + text);
		return x509Certificate2;
	}

	private static X509Certificate2 EnsureRootCa()
	{
		if (File.Exists(Settings.RootCaCert) && File.Exists(Settings.RootCaKey))
		{
			X509Certificate2 existing = X509Certificate2.CreateFromPemFile(Settings.RootCaCert, Settings.RootCaKey);
			if (!File.Exists(Settings.RootCaCertDer))
			{
				File.WriteAllBytes(Settings.RootCaCertDer, existing.Export(X509ContentType.Cert));
			}
			return existing;
		}
		using RSA rSA = RSA.Create(2048);
		CertificateRequest certificateRequest = new CertificateRequest("CN=BigBangRacing Offline Root CA", rSA, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
		certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.KeyCertSign, critical: true));
		certificateRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(certificateRequest.PublicKey, critical: false));
		using X509Certificate2 x509Certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1.0), DateTimeOffset.UtcNow.AddYears(10));
		File.WriteAllText(Settings.RootCaCert, x509Certificate.ExportCertificatePem());
		File.WriteAllText(Settings.RootCaKey, rSA.ExportPkcs8PrivateKeyPem());
		File.WriteAllBytes(Settings.RootCaCertDer, x509Certificate.Export(X509ContentType.Cert));
		Console.WriteLine("[Setup] Generated a new local Root CA (" + Settings.RootCaCert + ") - this install's own, never shared.");
		return X509Certificate2.CreateFromPemFile(Settings.RootCaCert, Settings.RootCaKey);
	}

	private static X509Certificate2 CreateLeaf(X509Certificate2 root)
	{
		using RSA rSA = RSA.Create(2048);
		CertificateRequest certificateRequest = new CertificateRequest("CN=" + Settings.TargetHosts[0], rSA, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, critical: false));
		certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature, critical: true));
		certificateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
		{
			new Oid("1.3.6.1.5.5.7.3.1")
		}, critical: false));
		SubjectAlternativeNameBuilder subjectAlternativeNameBuilder = new SubjectAlternativeNameBuilder();
		string[] targetHosts = Settings.TargetHosts;
		foreach (string dnsName in targetHosts)
		{
			subjectAlternativeNameBuilder.AddDnsName(dnsName);
		}
		certificateRequest.CertificateExtensions.Add(subjectAlternativeNameBuilder.Build());
		byte[] bytes = RandomNumberGenerator.GetBytes(16);
		bytes[0] &= 127;
		using X509Certificate2 certificate = certificateRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1.0), DateTimeOffset.UtcNow.AddDays(397.0), bytes);
		using X509Certificate2 x509Certificate = certificate.CopyWithPrivateKey(rSA);
		return new X509Certificate2(x509Certificate.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
	}
}
