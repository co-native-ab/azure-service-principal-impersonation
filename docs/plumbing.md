## Plumbing the project

### Create the project

```shell
func init src/ --worker-runtime dotnet-isolated --target-framework net9.0
dotnet new sln
dotnet sln add src/
dotnet new install xunit.v3.templates
dotnet new update
dotnet new xunit3 -o tests/
sed -i "s|<TargetFramework>net8.0</TargetFramework>|<TargetFramework>net9.0</TargetFramework>|g" tests/tests.csproj
sed -i "s|<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>|-->\n\t\t<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>\n\t\t<\!--|g" tests/tests.csproj
dotnet sln add tests/
dotnet add tests/ reference src/
```

### Configure gitignore

```shell
dotnet new gitignore
cat <<EOF >> .gitignore

# Terraform

$(curl -s https://raw.githubusercontent.com/github/gitignore/refs/heads/main/Terraform.gitignore)
EOF
cat <<EOF >> .gitignore

# Miscellaneous

.tmp/
EOF
```

### Create the first function

```shell
env --chdir=src/ func new --name Metadata --template "HTTP trigger" --namespace ASPI.Functions
```
