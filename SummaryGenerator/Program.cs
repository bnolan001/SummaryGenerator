using SummaryGenerator.Models;
using SummaryGenerator.Repositories.HuggingFace;
using SummaryGenerator.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<ModelsOptions>(builder.Configuration.GetSection(ModelsOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));
builder.Services.Configure<HuggingFaceOptions>(builder.Configuration.GetSection(HuggingFaceOptions.SectionName));
builder.Services.Configure<SummarizationOptions>(builder.Configuration.GetSection(SummarizationOptions.SectionName));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection(UploadOptions.SectionName));
builder.Services.AddScoped<IProgress, ProgressReporter>();
builder.Services.AddHttpClient<IHuggingFaceRepository, HuggingFaceRepository>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<ILlamaSharpSummarizer, LlamaSharpSummarizer>();
builder.Services.AddSingleton<IMarkdownOutputWriter, MarkdownOutputWriter>();
builder.Services.AddSingleton<IPromptProfileStore, PromptProfileStore>();
builder.Services.AddSingleton<IProcessingQueue, QueueProcessor>();
builder.Services.AddHostedService(provider => (QueueProcessor)provider.GetRequiredService<IProcessingQueue>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
