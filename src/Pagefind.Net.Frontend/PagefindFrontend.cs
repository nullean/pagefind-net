using System.IO.Abstractions;
using System.IO.Compression;

namespace Pagefind.Net.Frontend;

/// <summary>
/// Extracts the embedded Pagefind browser runtime (<c>pagefind.js</c> and
/// <c>wasm.en.pagefind</c>) to a target directory on disk.
/// </summary>
public static class PagefindFrontend
{
	/// <summary>
	/// The Pagefind version shipped in this package.
	/// </summary>
	public const string Version = "1.5.2";

	private static readonly System.Reflection.Assembly ResourceAssembly = typeof(PagefindFrontend).Assembly;

	private static readonly string[] Assets = ["pagefind.js", "wasm.en.pagefind"];

	/// <summary>
	/// Extracts the frontend runtime files into <paramref name="outputDirectory"/>.
	/// Files are only written if they do not already exist or the embedded version is newer
	/// (determined by comparing a version marker file with the current package version).
	/// </summary>
	/// <param name="fs">File system abstraction for testability.</param>
	/// <param name="outputDirectory">
	/// Directory to write into (e.g. <c>wwwroot/pagefind</c>). Created if it does not exist.
	/// </param>
	/// <param name="force">When <c>true</c>, always overwrite existing files.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The list of file paths that were written.</returns>
	public static async Task<string[]> ExtractToAsync(
		IFileSystem fs,
		string outputDirectory,
		bool force = false,
		CancellationToken ct = default)
	{
		fs.Directory.CreateDirectory(outputDirectory);

		var versionMarker = fs.Path.Combine(outputDirectory, ".pagefind-net-frontend-version");
		if (!force && fs.File.Exists(versionMarker))
		{
			var existing = fs.File.ReadAllText(versionMarker).Trim();
			if (existing == Version)
			{
				// All assets presumed up-to-date.
				var allExist = true;
				foreach (var asset in Assets)
				{
					if (!fs.File.Exists(fs.Path.Combine(outputDirectory, asset)))
					{
						allExist = false;
						break;
					}
				}
				if (allExist) return [];
			}
		}

		var written = new List<string>();
		foreach (var asset in Assets)
		{
			var targetPath = fs.Path.Combine(outputDirectory, asset);
			await ExtractResourceAsync(fs, $"{asset}.gz", targetPath, ct);
			written.Add(targetPath);
		}

		fs.File.WriteAllText(versionMarker, Version);
		return [.. written];
	}

	/// <summary>
	/// Convenience overload using the real file system.
	/// </summary>
	public static Task<string[]> ExtractToAsync(
		string outputDirectory,
		bool force = false,
		CancellationToken ct = default)
		=> ExtractToAsync(new FileSystem(), outputDirectory, force, ct);

	private static async Task ExtractResourceAsync(
		IFileSystem fs, string resourceName, string targetPath, CancellationToken ct)
	{
		await using var resourceStream = ResourceAssembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException(
				$"Embedded resource '{resourceName}' not found in {ResourceAssembly.GetName().Name}.");

		await using var gzip = new GZipStream(resourceStream, CompressionMode.Decompress);
		await using var output = fs.FileStream.New(targetPath, FileMode.Create, FileAccess.Write,
			FileShare.None, 65536, FileOptions.Asynchronous);
		await gzip.CopyToAsync(output, ct);
	}
}
