using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System;
using Microsoft.Extensions.Options;
using condominio_api.Services;
using condominio_api.Models;
using condominio_api.DependencyService;

namespace condominio_api
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            Settings.ValidateForEnvironment(environment.IsProduction());
            Console.Write("\nDate Up Server :" + DateTime.UtcNow+ "\n"+"\n");
        }
        
        public IConfiguration Configuration { get; }

        readonly string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
    
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddCors(options =>
                options.AddPolicy(MyAllowSpecificOrigins, builder =>
                    builder.WithOrigins(Settings.AllowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()));

            //Configurando o acesso ao MongoDb
            services.Configure<CondominioDatabaseSetting>(
                Configuration.GetSection(nameof(CondominioDatabaseSetting))
            );

            //Certificando de que ira existir uma unica instancia
            services.AddSingleton<ICondominioDatabaseSetting>(sp =>
                sp.GetRequiredService<IOptions<CondominioDatabaseSetting>>().Value);

            services.AddSingleton<IUserService, UserService>();
            services.AddSingleton<CondominioService>();
            services.AddSingleton<IPaymentService, PaymentService>();
            services.AddHostedService<CheckPayment>();

            services.AddControllers();

            //Adiciona o servico de autenticacao atraves do JsonWebToken
            var key = Encoding.UTF8.GetBytes(Settings.Secret);
            services.AddAuthentication(x => 
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateLifetime = true,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuer = false,
                    ValidateAudience = false
                };
            });

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseCors(MyAllowSpecificOrigins);
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
