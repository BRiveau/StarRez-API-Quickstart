# Starrez Api Quickstart Guide

## Technology Overview

- NX Monorepo is used for improving the developer experience of working with many projects in a single location ()
- ASP.NET Core 9.0 is the framework used to construct the reverse proxy and internal StarRez API applications ()
- YARP is the NuGet package used for managing the reverse proxy, allowing for a micro-services architecture on the backend ()

## Set Up for Development

### Software Requirements

- NodeJS (for monorepo/web development)
- NPM (for package management/monorepo work **This is included with a NodeJS install**)
- ASP.NET 9.0 SDK (for API development)

### Initial Setup

1. Run `npm i` from repository root to install all package dependencies
2. Create `.env` and `.env.production` files ((dotenvx) is recommended for environment variable management) and set the following values:
   - API_URL (The URL of the reverse proxy; For development, this should be `https://localhost:7040`)
   - STARREZ_API_URL (The base URL of your StarRez API server; This must be in the format of `https://starrezdomain.com/StarRezRest`)
   - STARREZ_API_USER (The username of the StarRez user that will be used for making certain API requests; By default, this user is only used for getting the StarRez API documentation)
   - STARREZ_API_KEY (The StarRez API key generated for the specified StarRez user)

## How to Use this Repository
