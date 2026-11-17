"""Validate the local OpenAPI 3.1 contract and actual HTTP response envelopes."""

import argparse
import json
from pathlib import Path
import re
import sys

from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import OpenAPIV31SpecValidator
from referencing import Registry
import yaml


ROOT = Path(__file__).resolve().parents[1]
REQUIRED_SAMPLES = {"Instructor", "Department", "Course", "InstructorPage", "DepartmentPage", "CoursePage"}
LOCAL_REF = re.compile(r"#/components/[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+\Z")


def check_references(value, document):
    """Reject remote refs before invoking any library capable of resolving refs."""
    if isinstance(value, dict):
        for key in ("$id", "$dynamicRef", "$recursiveRef"):
            if key in value:
                raise ValueError(f"{key} is not supported in this local-only contract")
        if "$ref" in value:
            ref = value["$ref"]
            if not isinstance(ref, str) or not LOCAL_REF.fullmatch(ref):
                raise ValueError(f"Only local component references are permitted: {ref!r}")
            target = document
            for segment in ref[2:].split("/"):
                if not isinstance(target, dict) or segment not in target:
                    raise ValueError(f"Unresolved reference: {ref}")
                target = target[segment]
        for child in value.values():
            check_references(child, document)
    elif isinstance(value, list):
        for child in value:
            check_references(child, document)


def no_remote(uri):
    raise ValueError(f"Remote schema retrieval is forbidden: {uri}")


def reject_constant(value):
    raise ValueError(f"Invalid JSON number: {value}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--spec", type=Path, default=ROOT / "contracts/academics.openapi.yaml")
    parser.add_argument("--samples", type=Path)
    args = parser.parse_args()
    document = yaml.safe_load(args.spec.read_text(encoding="utf-8"))
    if not isinstance(document, dict) or document.get("openapi") != "3.1.0":
        raise ValueError("An OpenAPI 3.1.0 document is required")
    if document.get("jsonSchemaDialect") != "https://json-schema.org/draft/2020-12/schema":
        raise ValueError("JSON Schema 2020-12 dialect is required")
    check_references(document, document)
    OpenAPIV31SpecValidator(document).validate()
    schemas = document["components"]["schemas"]
    for schema in schemas.values():
        Draft202012Validator.check_schema(schema)
    print(f"PASS: {args.spec} (OpenAPI 3.1 / JSON Schema 2020-12)")
    if args.samples is None:
        print("Samples not requested; this is document validation only.")
        return
    if not args.samples.is_dir():
        raise ValueError(f"Sample directory is missing: {args.samples}")
    files = sorted(args.samples.rglob("*.json"))
    if not files:
        raise ValueError("No JSON response samples found")
    seen = set()
    for path in files:
        envelope = json.loads(path.read_text(encoding="utf-8"), parse_constant=reject_constant)
        if not isinstance(envelope, dict) or set(envelope) != {"schema", "body"}:
            raise ValueError(f"{path}: expected exactly schema and body")
        name = envelope["schema"]
        if not isinstance(name, str) or name not in schemas:
            raise ValueError(f"{path}: unknown local schema {name!r}")
        # Keep components at the document root so ordinary local refs work.
        wrapper = {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "$ref": f"#/components/schemas/{name}",
            "components": document["components"],
        }
        validator = Draft202012Validator(
            wrapper, registry=Registry(retrieve=no_remote), format_checker=FormatChecker()
        )
        errors = sorted(validator.iter_errors(envelope["body"]), key=lambda e: str(e.json_path))
        if errors:
            raise ValueError(f"{path}: " + "; ".join(
                f"{error.json_path}: {error.message}" for error in errors
            ))
        seen.add(name)
    missing = REQUIRED_SAMPLES - seen
    if missing:
        raise ValueError(f"Missing sample categories: {', '.join(sorted(missing))}")
    print(f"PASS: all {len(files)} response samples; categories: {', '.join(sorted(seen))}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
