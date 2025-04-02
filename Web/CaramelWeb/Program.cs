using CaramelWeb.Components;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Numerics;
using Caramel;
using static CaramelWeb.Components.Pages.Home;

namespace CaramelWeb;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddFluentUIComponents();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}

public static class Extensions
{
    public static string Capitalize(this string input) =>
        input switch
        {
            null => "",
            "" => "",
            _ => string.Concat(input[0].ToString().ToUpper(), input.AsSpan(1))
        };

    public static Quaternion FromTwoVectors(Vector3 forward, Vector3 direction)
    {
        // Normalize the input vectors
        forward = Vector3.Normalize(forward);
        direction = Vector3.Normalize(direction);

        // Calculate the cross product and dot product
        var cross = Vector3.Cross(forward, direction);
        var dot = Vector3.Dot(forward, direction);

        switch (dot)
        {
            case > 0.9999f:
                return Quaternion.Identity;

            case < -0.9999f:
            {
                // Find a perpendicular vector to construct the quaternion
                var axis = Vector3.Cross(forward, Vector3.UnitX);
                if (axis.LengthSquared() < 0.0001f)
                {
                    axis = Vector3.Cross(forward, Vector3.UnitY);
                }

                axis = Vector3.Normalize(axis);
                return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
            }
        }

        return Quaternion.CreateFromAxisAngle(
            Vector3.Normalize(cross), MathF.Acos(dot));
    }

    public static DataVector Subtract(this DataVector k1, DataVector k2)
    {
        return new DataVector
        {
            X = k1.X - k2.X,
            Y = k1.Y - k2.Y,
            Z = k1.Z - k2.Z
        };
    }

    public static Vector3 Vec(this DataVector k1)
    {
        return new Vector3
        {
            X = k1.X,
            Y = k1.Y,
            Z = k1.Z
        };
    }

    public static DataQuaternion Ame(this Quaternion k1)
    {
        return new DataQuaternion
        {
            X = k1.X,
            Y = k1.Y,
            Z = k1.Z,
            W = k1.W
        };
    }
}
