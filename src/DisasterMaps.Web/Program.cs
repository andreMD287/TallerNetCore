using DisasterMaps.Web.Clients;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
if (builder.Configuration.GetValue<bool>("Business:UseFake"))
    builder.Services.AddSingleton<IBusinessApiClient, FakeBusinessApiClient>();
else
    builder.Services.AddHttpClient<IBusinessApiClient, BusinessApiClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Business:BaseUrl"] ?? "http://localhost:5002");
        client.Timeout = TimeSpan.FromSeconds(5);
    });
var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Home/Error");
// HTTP puro en desarrollo y compose; no forzar redirección a HTTPS.
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Map}/{action=Index}/{id?}");
app.Run();
