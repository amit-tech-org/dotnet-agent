using System.Net;
using System.Net.Http.Headers;
using Parquet;
using Parquet.Schema;

namespace AI.Agent.ConsoleApp
{
    public sealed record GaiaProblem(
        string? TaskId,
        string? Question,
        int? Level,
        string? FinalAnswer,
        string? FileName,
        string? FilePath);

    public static class GaiaDataset
    {
        public static async Task<IReadOnlyList<GaiaProblem>> LoadAsync(
            string path,
            string name,
            string split,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(path, "gaia-benchmark/GAIA", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Only gaia-benchmark/GAIA is supported, not '{path}'.", nameof(path));
            }

            string relativePath = (name, split) switch
            {
                ("2023_level1", "validation") => "2023/validation/metadata.level1.parquet",
                ("2023_level1", "test") => "2023/test/metadata.level1.parquet",
                ("2023_level2", "validation") => "2023/validation/metadata.level2.parquet",
                ("2023_level2", "test") => "2023/test/metadata.level2.parquet",
                ("2023_level3", "validation") => "2023/validation/metadata.level3.parquet",
                ("2023_level3", "test") => "2023/test/metadata.level3.parquet",
                ("2023_all", "validation") => "2023/validation/metadata.parquet",
                ("2023_all", "test") => "2023/test/metadata.parquet",
                _ => throw new ArgumentException($"GAIA has no '{name}' config split '{split}'.")
            };

            string parquetPath = await DownloadAsync(relativePath, cancellationToken);
            return await ReadProblemsAsync(parquetPath, cancellationToken);
        }

        private static async Task<string> DownloadAsync(string relativePath, CancellationToken cancellationToken)
        {
            string cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache",
                "huggingface",
                "datasets",
                "gaia-benchmark",
                "GAIA",
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            {
                return cachePath;
            }

            string? token = FirstNonEmpty(
                Environment.GetEnvironmentVariable("HF_TOKEN"),
                Environment.GetEnvironmentVariable("HUGGING_FACE_HUB_TOKEN"));
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "GAIA is a gated dataset. Accept access at https://huggingface.co/datasets/gaia-benchmark/GAIA and set HF_TOKEN or HUGGING_FACE_HUB_TOKEN.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            string temporaryPath = cachePath + ".download";
            var url = new Uri($"https://huggingface.co/datasets/gaia-benchmark/GAIA/resolve/main/{relativePath}");

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AI.Agent.Console/1.0");

            try
            {
                for (int hop = 0; hop < 10; hop++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (url.Host.EndsWith("huggingface.co", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }

                    using HttpResponseMessage response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        throw new InvalidOperationException(
                            "Hugging Face denied access to gaia-benchmark/GAIA. Accept the dataset terms and use an HF_TOKEN that has access.");
                    }

                    if ((int)response.StatusCode is >= 300 and < 400)
                    {
                        if (response.Headers.Location is not Uri location)
                        {
                            throw new InvalidOperationException("Hugging Face returned a redirect without a location.");
                        }

                        url = location.IsAbsoluteUri ? location : new Uri(url, location);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    await using (FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(output, cancellationToken);
                    }

                    File.Move(temporaryPath, cachePath, overwrite: true);
                    return cachePath;
                }

                throw new InvalidOperationException("Too many redirects while downloading the GAIA dataset.");
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static async Task<IReadOnlyList<GaiaProblem>> ReadProblemsAsync(
            string parquetPath,
            CancellationToken cancellationToken)
        {
            await using ParquetReader reader = await ParquetReader.CreateAsync(parquetPath, cancellationToken: cancellationToken);
            var problems = new List<GaiaProblem>();

            for (int groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
            {
                using ParquetRowGroupReader group = reader.OpenRowGroupReader(groupIndex);
                int rowCount = checked((int)group.RowCount);
                if (rowCount == 0)
                {
                    continue;
                }

                string?[] taskIds = await ReadStringsAsync(group, reader.Schema, "task_id", rowCount, cancellationToken);
                string?[] questions = await ReadStringsAsync(group, reader.Schema, "Question", rowCount, cancellationToken);
                int?[] levels = await ReadLevelsAsync(group, reader.Schema, rowCount, cancellationToken);
                string?[] answers = await ReadStringsAsync(group, reader.Schema, "Final answer", rowCount, cancellationToken);
                string?[] fileNames = await ReadStringsAsync(group, reader.Schema, "file_name", rowCount, cancellationToken);
                string?[] filePaths = await ReadStringsAsync(group, reader.Schema, "file_path", rowCount, cancellationToken);

                for (int row = 0; row < rowCount; row++)
                {
                    problems.Add(new GaiaProblem(
                        taskIds[row],
                        questions[row],
                        levels[row],
                        answers[row],
                        fileNames[row],
                        filePaths[row]));
                }
            }

            return problems;
        }

        private static async Task<string?[]> ReadStringsAsync(
            ParquetRowGroupReader group,
            ParquetSchema schema,
            string columnName,
            int rowCount,
            CancellationToken cancellationToken)
        {
            DataField? field = FindField(schema, columnName);
            if (field is null)
            {
                return new string?[rowCount];
            }

            if (field.ClrType == typeof(ReadOnlyMemory<char>))
            {
                ReadOnlyMemory<char>?[] raw = await ReadNullableAsync<ReadOnlyMemory<char>>(
                    group,
                    field,
                    rowCount,
                    cancellationToken);
                var values = new string?[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    values[i] = raw[i]?.Span.ToString();
                }

                return values;
            }

            if (field.ClrType != typeof(string))
            {
                throw new InvalidDataException($"Column '{columnName}' is {field.ClrType}, not a string.");
            }

            var strings = new string?[rowCount];
            await group.ReadAsync(field, strings.AsMemory(), cancellationToken: cancellationToken);
            return strings;
        }

        private static async Task<int?[]> ReadLevelsAsync(
            ParquetRowGroupReader group,
            ParquetSchema schema,
            int rowCount,
            CancellationToken cancellationToken)
        {
            DataField? field = FindField(schema, "Level");
            if (field is null)
            {
                return new int?[rowCount];
            }

            if (field.ClrType == typeof(int))
            {
                return await ReadNullableAsync<int>(group, field, rowCount, cancellationToken);
            }

            if (field.ClrType == typeof(long))
            {
                long?[] values = await ReadNullableAsync<long>(group, field, rowCount, cancellationToken);
                return values.Select(value => value is null ? null : (int?)Convert.ToInt32(value)).ToArray();
            }

            if (field.ClrType == typeof(short))
            {
                short?[] values = await ReadNullableAsync<short>(group, field, rowCount, cancellationToken);
                return values.Select(value => (int?)value).ToArray();
            }

            if (field.ClrType == typeof(string) || field.ClrType == typeof(ReadOnlyMemory<char>))
            {
                string?[] values = await ReadStringsAsync(group, schema, "Level", rowCount, cancellationToken);
                return values.Select(value => int.TryParse(value, out int level) ? (int?)level : null).ToArray();
            }

            throw new InvalidDataException($"Column 'Level' has unsupported type {field.ClrType}.");
        }

        private static async Task<T?[]> ReadNullableAsync<T>(
            ParquetRowGroupReader group,
            DataField field,
            int rowCount,
            CancellationToken cancellationToken)
            where T : struct
        {
            var values = new T?[rowCount];
            await group.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);
            return values;
        }

        private static DataField? FindField(ParquetSchema schema, string columnName)
        {
            try
            {
                return schema.FindDataField(columnName);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
