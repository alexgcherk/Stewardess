"""Sample Python module for testing the heuristic parser."""

import os
import sys
from typing import Optional, List


CONSTANT_VALUE = 42
MODULE_NAME = "sample_module"


class Animal:
    """Base class for animals."""

    def __init__(self, name: str, sound: str):
        self.name = name
        self.sound = sound

    def speak(self) -> str:
        return f"{self.name} says {self.sound}"

    @staticmethod
    def from_dict(data: dict) -> "Animal":
        return Animal(data["name"], data["sound"])

    class Meta:
        abstract = True


class Dog(Animal):
    """A dog — man's best friend."""

    def __init__(self, name: str):
        super().__init__(name, "woof")

    def fetch(self, item: str) -> str:
        return f"{self.name} fetches the {item}"


def greet(name: str, greeting: str = "Hello") -> str:
    """Return a greeting string."""
    return f"{greeting}, {name}!"


async def fetch_data(url: str) -> Optional[str]:
    """Async function to fetch data from a URL."""
    return None


def _private_helper(value: int) -> int:
    return value * 2
