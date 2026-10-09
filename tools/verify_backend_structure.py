"""Offline structural checks; does NOT replace dotnet build/test or DB integration tests."""
from pathlib import Path
from xml.etree import ElementTree
import json

root = Path(__file__).resolve().parents[1]
required = [
    'src/HipoSim.Api/HipoSim.Api.csproj',
    'src/HipoSim.Api/Program.cs',
    'src/HipoSim.Api/Auth/AuthController.cs',
    'src/HipoSim.Api/Auth/AuthService.cs',
    'src/HipoSim.Api/Data/HipoSimDbContext.cs',
    'src/HipoSim.Api/Data/Migrations/202610090001_InitialPlatform.cs',
    'src/HipoSim.Api/Data/DesignTimeDbContextFactory.cs',
    'src/HipoSim.Api/Data/EfSimulationRepository.cs',
    'tests/HipoSim.Api.Tests/AuthTests.cs',
    '.github/workflows/api-ci.yml',
    'docs/api-contract/openapi.v0.yaml',
]
for relative in required:
    assert (root / relative).is_file(), f'Missing {relative}'
for path in root.rglob('*.csproj'):
    ElementTree.parse(path)
for path in (root / 'src/HipoSim.Api').glob('appsettings*.json'):
    json.loads(path.read_text(encoding='utf-8'))
program = (root / 'src/HipoSim.Api/Program.cs').read_text()
assert 'AddSimulations(' in program
assert 'AddAuthentication(' in program
assert 'UseNpgsql(' in program
assert 'UseAuthentication()' in program
contract = (root / 'docs/api-contract/openapi.v0.yaml').read_text()
for endpoint in ('/health:', '/api/auth/register:', '/api/auth/login:', '/api/simulations:'):
    assert endpoint in contract
sln = (root / 'HipoSim.FinancialEngine.sln').read_text(encoding='utf-8-sig')
assert 'HipoSim.Api.csproj' in sln and 'HipoSim.Api.Tests.csproj' in sln
print(f'PASS offline structure: {len(required)} mandatory files, XML projects, JSON settings, API registrations & contract routes.')
print('NOT RUN: dotnet compilation, .NET test suite, PostgreSQL migration, remote deployment.')
