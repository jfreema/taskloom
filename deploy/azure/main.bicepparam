using './main.bicep'

param appName = 'taskloom'
// Pass the password on the command line or from Key Vault instead of committing it:
//   az deployment group create ... -p postgresAdminPassword=<value>
param postgresAdminPassword = readEnvironmentVariable('TASKLOOM_PG_PASSWORD', '')
param allowedEmailDomains = []
