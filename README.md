# Starrez Api Quickstart Guide

## Introduction

This repository is meant to allow for quick configuration and integration with the StarRez API. This is done through a reverse proxy and custom StarRez project, which allows for access to all StarRez API endpoints by default. Due to the presence of a custom StarRez project, it is extremely easy to develop custom StarRez API logic that extends the default capabilities of the API. The architecture and technologies used in this repository are also extremely modular, allowing development of additional applications that use any other modern programming language/framework.

### Technology Overview

- NX Monorepo is used for improving the developer experience of working with many projects in a single location ([NX Documentation](https://nx.dev/getting-started/intro))
- ASP.NET Core 9.0 is the framework used to construct the reverse proxy and internal StarRez API applications ([ASP.NET Core Documentation](https://learn.microsoft.com/en-us/aspnet/core/?view=aspnetcore-9.0))
- YARP is the NuGet package used for managing the reverse proxy, allowing for a micro-services architecture on the backend ([YARP Documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-9.0))

## Features

- Preconfigured reverse-proxy application for quick integration with existing APIs
- Request/response logging of all reverse-proxy requests using [Serilog](https://serilog.net/)
- Template application for creating custom StarRez API logic
- Self-hosted API documentation website using [Scalar](https://scalar.com/)
- Updates StarRez API documentation to meet HTTP standards
- Optionally include latest StarRez database schemas in API documentation website (significantly slows down initial page load)
- Automatically generates API documentation using OpenAPI

## Installation

### Software Requirements

- [NodeJS](https://nodejs.org/en/download) (for monorepo/web development)
- NPM (for package management/monorepo work; **This is included with a NodeJS install**)
- [ASP.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download) (for API development)
- StarRez with API package

### StarRez Setup

1. Create permissions group for API access
2. Generate API token for at least one user ([How to Create a StarRez API Token](https://support.starrez.com/hc/en-us/articles/208606766-Create-Token-for-REST-API-calls))

### Initial Setup

1. Fork this repository ([How to Fork a GitHub Repository](https://www.geeksforgeeks.org/git/how-to-fork-a-github-repository/))
2. Run `npm i` from repository root to install all package dependencies
3. Create `.env` and `.env.production` files ([Dotenvx](https://dotenvx.com/) is recommended for environment variable management) and set the following values:
   - API_URL (The URL of the reverse proxy; For development, this should be `https://localhost:7040`)
   - STARREZ_API_URL (The base URL of your StarRez API server; This must be in the format of `https://starrezdomain.com/StarRezRest`)
   - STARREZ_API_USER (The username of the StarRez user that will be used for making certain API requests; By default, this user is only used for getting the StarRez API documentation)
   - STARREZ_API_KEY (The StarRez API key generated for the specified StarRez user)

## How to Use this Repository

### Serve Development API

Run `dotenvx run -- npx nx serve reverse-proxy` to run the development version of the API, which will be hosted on `https://localhost:7040`. To access API documentation, navigate to `https://localhost:7040/docs`. To make requests to the API, use the default server URL and add the specified path to the end of the URL (for StarRez API requests, add `/starrez/` before the StarRez API path).
