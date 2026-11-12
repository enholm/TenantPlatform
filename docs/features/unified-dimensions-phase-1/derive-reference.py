#!/usr/bin/env python3
"""Frozen phase-1 evidence derivation. Python stdlib + read-only local Git only.

Run from any directory: python3 path/to/derive-reference.py [--check]
Reads the pinned legacy commit, never the future/current generator. The arithmetic
is a reviewed transcription of SeedMagidaAsync, not execution/parsing of C# code.
Names and literal arrays are extracted from that frozen source. No .NET, EF,
application services, configuration, database, network, or initializer is loaded.
--check compares bytes in memory and writes nothing.
"""
import calendar
import collections
import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

COMMIT = 'f2f2d67338fd05e59624423aad675ec4f8ca415d'
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
INIT = 'src/TenantPlatform.Infrastructure/Initialization/'
MIGRATIONS = 'src/TenantPlatform.Infrastructure/Persistence/Migrations/'


def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args])


def source(path):
    return git('show', f'{COMMIT}:{path}').decode('utf-8-sig')


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def sid(kind, number):
    return f'b0000000-{kind:04x}-0000-0000-{number:012x}'


def add_months(day, months):
    year, month = divmod(day.year * 12 + day.month - 1 + months, 12)
    month += 1
    return datetime.date(year, month, min(day.day, calendar.monthrange(year, month)[1]))


def strings(name):
    body = re.search(r'string\[\] ' + name + r' = \[(.*?)\];', demo, re.S).group(1)
    return re.findall(r'"([^"]*)"', body)


def default_properties(path, class_name):
    # Only simple auto-properties in these frozen entity classes. Explicitly fail
    # for any initializer this evidence script has not accounted for.
    body = source(path).split('class ' + class_name + '\n{', 1)[1].split('\n}', 1)[0]
    props = {}
    for typename, name, initializer in re.findall(
            r'public ([\w<>?\[\]]+) (\w+) \{ get; set; \}(?: = ([^;]+);)?', body):
        if initializer:
            if initializer in ('string.Empty', '""'): value = ''
            elif initializer == '[]': value = []
            elif initializer in ('true', 'false'): value = initializer == 'true'
            else: value = initializer.split('.')[-1]  # Frozen enum default.
        elif typename.endswith('?') or typename.startswith('List<'): value = None
        elif typename == 'bool': value = False
        elif typename == 'Guid': value = '00000000-0000-0000-0000-000000000000'
        elif typename == 'DateOnly': value = '0001-01-01'
        elif typename == 'DateTimeOffset': value = '0001-01-01T00:00:00+00:00'
        elif typename in ('int', 'decimal'): value = 0
        elif typename.startswith('Agreement'): value = 0  # default enum numeric zero
        else: raise ValueError((class_name, typename, name))
        props[name] = value
    assert props, class_name
    return props


demo = source(INIT + 'DemoDataInitializer.cs')
seed_ids = source(INIT + 'SeedIds.cs')
assert 'for (var i = 0; i < 300; i++)' in demo
assert 'Guid.Parse($"b0000000-{entityKind:x4}-0000-0000-{number:x12}")' in seed_ids
assert 'var department = i / 12 + 1;' in demo
assert 'var locationNumber = c * 4 + country + 1;' in demo
continents_block = demo.split('var continents = ', 1)[1].split('var areas = ', 1)[0]
continents = []
for name, code, value, countries_body in re.findall(
        r'\("([^"]+)", "([^"]+)", ([\d.]+)m, \[(.*?)\]\)', continents_block):
    continents.append((name, code, value, re.findall(r'\("([^"]+)", "([^"]+)"\)', countries_body)))
assert len(continents) == 4 and all(len(x[3]) == 4 for x in continents)
account = sid(1, 1)
actor = '$resolvedMagidaUserId'
recorded = '$recordedUtc'
core = 'src/TenantPlatform.Core/Agreements/'
geo, dept, counterparties, indices, index_values = [], [], [], [], []


def node(rows, kind, number, name, parent, level):
    rows.append(dict(id=sid(kind, number), stable_key=f'Magida{kind}:{number}',
                     seed_symbol=f'{ {6:"MagidaDivision",7:"MagidaDepartment",8:"MagidaContinent",9:"MagidaCountry",10:"MagidaCapital"}[kind]}({number})',
                     account_id=account, name=name, parent_id=parent, level=level,
                     generation_order=len(rows) + 1, description=None, is_active=True))


for division, name in enumerate(strings('divisions'), 1):
    node(dept, 6, division, f'Divisjon {division} – {name}', None, 1)
    for department in range(1, 6):
        node(dept, 7, (division - 1) * 5 + department, f'Avdeling {division}.{department}', sid(6, division), 2)
for c, (name, code, value, countries) in enumerate(continents):
    node(geo, 8, c + 1, name, None, 1)
    indices.append(dict(Id=sid(12, c + 1), AccountId=account, Code=f'MAGIDA-{code}',
                        Name=f'Magida {name} årsindeks', Resolution='Year',
                        Description='Syntetisk demoindeks med basis 100 i 2026.', Source='Magida demodata',
                        RecordedUtc=recorded, ActorUserId=actor))
    for year in (2026, 2027):
        value_id = sid(13, c * 2 + year - 2026 + 1)
        index_values.append(dict(Id=value_id, AccountId=account, IndexId=sid(12, c + 1),
                                 PeriodKey=value_id, Period=f'{year}-01-01', Revision=1,
                                 Value='100' if year == 2026 else value, RecordedUtc=recorded,
                                 ActorUserId=actor, Reason='Syntetisk demoverdi', Superseded=False, PublishedDate=None))
    for country, (country_name, capital) in enumerate(countries):
        number = c * 4 + country + 1
        node(geo, 9, number, country_name, sid(8, c + 1), 2)
        node(geo, 10, number, capital, sid(9, number), 3)
        counterparties.append(dict(Id=sid(11, number), AccountId=account,
                                   Name=f'Demo Partner {capital}', Type='ServiceProvider', IsActive=True))
by_id = {n['id']: n for n in geo + dept}
for n in geo + dept:
    ancestors = []
    parent = n['parent_id']
    while parent:
        ancestors.insert(0, parent)
        parent = by_id[parent]['parent_id']
    n['ancestor_ids'] = ancestors
    n['path'] = ' / '.join(by_id[p]['name'] for p in ancestors + [n['id']])

types = strings('typeNames')
primary, secondary = strings('primaryLines'), strings('secondaryLines')
durations = [int(x) for x in re.search(r'int\[\] durations = \[(.*?)\]', demo).group(1).split(',')]
frequencies = re.findall(r'AgreementFrequency\.(\w+)', demo.split('AgreementFrequency[] frequencies = ', 1)[1].split(';', 1)[0])
agreement_defaults = default_properties(core + 'Agreement.cs', 'Agreement')
line_defaults = default_properties(core + 'AgreementLine.cs', 'AgreementLineVersion')
price_defaults = default_properties(core + 'AgreementLine.cs', 'AgreementPriceVersion')
agreements = []
for i in range(300):
    number = i + 1
    department = i // 12 + 1
    c = i % 4
    country = (i // 4) % 4
    location = c * 4 + country + 1
    kind = (i % 12 + department - 1) % 5
    start = datetime.date(2026, i % 6 + 1, 1)
    duration = durations[(i // 3) % len(durations)]
    form = ('FixedTerm', 'Renewing', 'Ongoing')[i % 3]
    end = (add_months(start, duration) - datetime.timedelta(days=1)).isoformat() if form == 'FixedTerm' else None
    renewal = add_months(start, duration) if form == 'Renewing' else None
    agreement_id = sid(14, number)
    props = dict(agreement_defaults)
    props.update(Id=agreement_id, AccountId=account,
                 Title=f'MAG-{number:03d} {types[kind]} – {continents[c][3][country][1]}',
                 Description=f'Demonstrasjonsavtale for Magida, avdeling {(department-1)//5+1}.{(department-1)%5+1}.',
                 Type=('Lease', 'SoftwareLicense', 'ServiceMaintenance', 'Supplier', 'Other')[kind],
                 Direction='Income' if (i // 4) % 2 == 0 else 'Cost', Currency='NOK',
                 IndexId=sid(12, c + 1), CounterpartyOrganizationId=sid(11, location), OwnerUserId=actor,
                 Status='Active', StartDate=start.isoformat(), EndDate=end, Form=form,
                 CurrentPeriodStartDate=start.isoformat(), RenewalDate=renewal.isoformat() if renewal else None,
                 AutoRenew=form == 'Renewing', RenewalMonths=duration if renewal else None,
                 NoticeMode='BeforeRenewal' if renewal else 'None', NoticeCount=None if form == 'FixedTerm' else 3,
                 NoticeUnit=None if form == 'FixedTerm' else 'Months',
                 NoticeDeadline=add_months(renewal, -3).isoformat() if renewal else None,
                 ReminderMode='Disabled', OrganizationElementId=sid(7, department), GeographicAreaId=sid(10, location),
                 CreatedUtc=recorded, UpdatedUtc=recorded, CreatedByUserId=actor, UpdatedByUserId=actor, Revision=agreement_id)
    selections = dict(Id=sid(15, number), AccountId=account, AgreementId=agreement_id,
                      IndexId=sid(12, c + 1), Sequence=1, EffectiveFrom=start.isoformat(), RecordedUtc=recorded, ActorUserId=actor)
    lines = []
    for line in range(2):
        line_number = i * 2 + line + 1
        line_id = sid(16, line_number)
        version = dict(line_defaults)
        version.update(Id=sid(17, line_number), AccountId=account, AgreementId=agreement_id,
                       LineId=line_id, Sequence=1, EffectiveFrom=start.isoformat(), RecordedUtc=recorded, ActorUserId=actor,
                       Name=(primary if line == 0 else secondary)[kind], StartDate=start.isoformat(), EndDate=end,
                       FirstPayableDate=start.isoformat(), Frequency=frequencies[(i // 4 + line) % len(frequencies)],
                       Anchor='Calendar', AnchorDate='0001-01-01', BillingTiming='Advance' if line == 0 else 'Arrears',
                       Status='Active', IndexRegulated=True, Reason='Opprettet som demodata')
        price = dict(price_defaults)
        price.update(Id=sid(18, line_number), AccountId=account, AgreementId=agreement_id, LineId=line_id,
                     Sequence=1, EffectiveFrom=start.isoformat(), IndexBaseDate=start.isoformat(),
                     Quantity=1 + i % 10 if line == 0 else 1,
                     UnitPrice=5000 + i % 40 * 1250 if line == 0 else 750 + i % 20 * 250,
                     RecordedUtc=recorded, ActorUserId=actor, Reason='Opprettet som demodata')
        lines.append(dict(entity=dict(Id=line_id, AccountId=account, AgreementId=agreement_id, ActivatedUtc=recorded),
                          version=version, price=price))
    agreements.append(dict(number=number, source_index=i, properties=props,
                           geography_id=props['GeographicAreaId'], organization_element_id=props['OrganizationElementId'],
                           geography_membership=by_id[props['GeographicAreaId']]['ancestor_ids'] + [props['GeographicAreaId']],
                           organization_membership=by_id[props['OrganizationElementId']]['ancestor_ids'] + [props['OrganizationElementId']],
                           index_selection=selections, lines=lines))


def distributions(nodes, field):
    explicit = collections.Counter(a[field] for a in agreements)
    members = collections.Counter()
    for a in agreements:
        n = by_id[a[field]]
        members.update(n['ancestor_ids'] + [n['id']])
    return [dict(id=n['id'], name=n['name'], level=n['level'], path=n['path'],
                 explicit_agreements=explicit[n['id']], inclusive_unique_agreements=members[n['id']]) for n in nodes]


assert len(geo) == 36 and len(dept) == 30 and len(agreements) == 300
assert len({a['properties']['Id'] for a in agreements}) == 300
assert all(len(a['lines']) == 2 for a in agreements)
assert [sum(n['level'] == level for n in geo) for level in (1, 2, 3)] == [4, 16, 16]
assert [sum(n['level'] == level for n in dept) for level in (1, 2)] == [5, 25]
geo_dist = distributions(geo, 'geography_id')
dept_dist = distributions(dept, 'organization_element_id')
assert all(d['inclusive_unique_agreements'] == 75 for d in geo_dist if d['level'] == 1)
assert all(d['explicit_agreements'] == 12 for d in dept_dist if d['level'] == 2)
assert all(d['inclusive_unique_agreements'] == 60 for d in dept_dist if d['level'] == 1)
assert sorted(d['explicit_agreements'] for d in geo_dist if d['level'] == 3) == [18]*4 + [19]*12

files = git('ls-tree', '-r', '--name-only', COMMIT).decode().splitlines()
source_paths = [p for p in files if p.startswith(INIT)] + [
    core + p for p in ('Agreement.cs', 'AgreementEnums.cs', 'AgreementNotice.cs', 'AgreementDeadline.cs',
                       'AgreementLine.cs', 'AgreementIndexSelection.cs', 'AgreementFinancialProcessing.cs')] + [
    'src/TenantPlatform.Core/Accounts/GeographicArea.cs',
    'src/TenantPlatform.Core/Accounts/OrganizationElement.cs',
    'src/TenantPlatform.Core/Accounts/Account.cs',
    'src/TenantPlatform.Core/Organizations/Organization.cs',
    'src/TenantPlatform.Core/Organizations/OrganizationType.cs',
    'src/TenantPlatform.Core/Localization/SupportedLanguages.cs']
manifest = {p: digest(git('show', f'{COMMIT}:{p}')) for p in source_paths}
reference = dict(schema_version=1, source_commit=COMMIT, derivation='Frozen Python transcription of SeedMagidaAsync; literals from pinned Git source; never executes initializer.',
                 source_sha256=manifest,
                 scope='Fresh successful demo initialization only; 300 Agreements for Magida, zero for Nordic Property; Leasing entities are separate.',
                 tokens={actor: 'Actual existing/new Magida administrator User.Id resolved by ID or email; fresh graph uses ' + sid(2, 1),
                         recorded: 'One DateTimeOffset.UtcNow captured in SeedMagidaAsync; same across its newly generated graph.'},
                 value_encoding='Enums use C# member names (default enum zero stays numeric 0); index decimals use exact decimal strings; monetary integer inputs are exact JSON numbers; dates ISO 8601.',
                 identity_format='b0000000-{entityKind:x4}-0000-0000-{number:x12}; decimal entity kinds 6..18; no RNG or seed input.',
                 account=dict(id=account, name='Magida', default_language='nb-NO'),
                 counts=dict(geography_values=36, geography_by_level=[4, 16, 16], organization_values=30,
                             organization_by_level=[5, 25], agreements=300, explicit_geography_assignments=300,
                             explicit_organization_assignments=300, counterparties=16, indices=4, index_values=8,
                             index_selections=300, lines=600, line_versions=600, price_versions=600),
                 geography=geo, organization_structure=dept, agreements=agreements,
                 counterparties=counterparties, indices=indices, index_values=index_values,
                 distributions=dict(geography=geo_dist, organization_structure=dept_dist),
                 generation_formulas=dict(department='i // 12 + 1', continent='i % 4', country='(i // 4) % 4',
                                          location='(i % 4) * 4 + (i // 4) % 4 + 1', type='(i % 12 + department - 1) % 5',
                                          bounds='i = 0..299; division=(department-1)//5+1'),
                 runtime_limits=[
                     'Existing agreements with reserved IDs are skipped together with their financial graph; actual insert count is 300 minus matching Magida IDs, not total stored count.',
                     'Existing hierarchy and counterparty/index rows are preserved by AddMissingMagidaEntitiesAsync; source cannot prove stored names, relationships or classification equivalence.',
                     'Owner identity may be reused by email. Runtime timestamps and audit UUIDs are not deterministically knowable.',
                     'This reference excludes later deadline initialization/workers and proves no PostgreSQL or database-backed idempotency behavior.'])

# Whole tracked checkout: exact symbols + table aliases, old type/table names,
# routes and dynamically used classification resource keys. Each hit has path,
# line and source. Broad names such as Organization/Location are reviewed in the
# report and are NOT automatically classified as removal candidates.
pattern = re.compile(r'GeographicArea|OrganizationElement|geographic_areas|organization_elements|'
                     r'AccountRegister(?:Input|Hierarchy)|AccountHierarchy(?:Row|Exception)|'
                     r'AgreementInsightDimension|GroupHierarchy|DashboardDimension(?:Geography|Organization)|'
                     r'AccountHierarchy|settings/(?:geography|organization-structure)|'
                     r'\b(?:Department|Departments|Location|Locations|CreateDepartment|EditDepartment|NoDepartments|CreateLocation|EditLocation|NoLocations|DepartmentsDescription|LocationsDescription)\b|\b(?:departments|locations)\b')
inventory = []
for path in files:
    if not path.endswith(('.cs', '.razor', '.resx', '.cshtml', '.md', '.json', '.csproj', '.sln', '.sh', '.yml', '.yaml')):
        continue
    raw = git('show', f'{COMMIT}:{path}')
    try: body = raw.decode('utf-8-sig')
    except UnicodeDecodeError: continue
    hits = [dict(line=i, source=line, matches=sorted(set(m.group(0) for m in pattern.finditer(line))))
            for i, line in enumerate(body.splitlines(), 1) if pattern.search(line)]
    if not hits: continue
    if path.endswith('ModelSnapshot.cs'): category = 'active_model_snapshot_replace_legacy'
    elif path.startswith(MIGRATIONS): category = 'historical_migration_preserve_unchanged'
    elif path.startswith('docs/'): category = 'documentation_review'
    elif path.startswith('tests/'): category = 'tests_review_actual_dependencies'
    elif path.endswith('.resx'): category = 'localization_review_call_sites'
    elif path.startswith('src/Serga.Website/'): category = 'unrelated_marketing_preserve'
    elif re.search(r'GeographicArea|OrganizationElement|AgreementInsightDimension|AccountRegister|AccountHierarchy|settings/(geography|organization-structure)', body):
        category = 'active_code_replace_legacy_parts_preserve_unrelated'
    else: category = 'other_location_or_department_review_preserve_unrelated'
    inventory.append(dict(path=path, category=category, hits=hits))
legacy_inventory = dict(source_commit=COMMIT, search_pattern=pattern.pattern,
                        method='All tracked files at pinned commit (including historical migrations and current snapshot); per-line static matches; report supplies semantic classification.',
                        files=inventory)
migration_hashes = {p: digest(git('show', f'{COMMIT}:{p}')) for p in files if p.startswith(MIGRATIONS)}
outputs = {'demo-reference.json': reference, 'legacy-inventory.json': legacy_inventory,
           'migration-source-sha256.json': dict(source_commit=COMMIT, algorithm='SHA-256 of exact Git blob bytes', files=migration_hashes)}
check = sys.argv[1:] == ['--check']
if sys.argv[1:] not in ([], ['--check']): raise SystemExit('Usage: derive-reference.py [--check]')
for name, value in outputs.items():
    encoded = (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode()
    target = HERE / name
    if check:
        if not target.is_file() or target.read_bytes() != encoded: raise SystemExit('MISMATCH: ' + name)
    else: target.write_bytes(encoded)
    print(('MATCH' if check else 'WROTE'), name, len(encoded), 'bytes; SHA256', digest(encoded))
print('Checked 36 geography values, 30 organization values, 300 complete agreement mappings and 600 financial lines.')
